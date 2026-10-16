using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CP2077SaveEditor.Utils;

/// <summary>What the shipped map knows about one TweakDBID.</summary>
public sealed class ImageEntry
{
    [JsonPropertyName("n")]
    public string Name { get; set; }

    [JsonPropertyName("u")]
    public string Url { get; set; }

    /// <summary>
    /// How confident the name match was, for vehicles only (weapons are matched on an
    /// exact id and carry no score). Below 1.0 means the picture is of the right model
    /// but possibly the wrong variant.
    /// </summary>
    [JsonPropertyName("s")]
    public double Score { get; set; } = 1.0;
}

/// <summary>
/// Pictures for vehicles and iconic weapons.
///
/// The id -> image-url map is generated offline by tools/build-image-map.py and shipped
/// inside the exe, so nothing is scraped at runtime. The only network traffic is
/// fetching the bytes of an image the user actually looks at, once, after which it
/// lives in %LOCALAPPDATA%. Everything here fails quietly: no map, no network, no disk
/// and the editor simply shows no picture.
/// </summary>
public static class ImageLibrary
{
    private const string ResourceName = "CP2077SaveEditor.Resources.ImageMap.json";
    private const int MaxWidth = 640;      // source renders are ~1860px; nobody needs that
    private const int MemoryLimit = 24;    // decoded bitmaps held at once

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Image> Memory = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> Recent = new();
    private static readonly HashSet<string> Failed = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> InFlight = new(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, ImageEntry> _map;
    private static HttpClient _http;
    private static string _cacheDir;

    /// <summary>Raised when an image finishes loading. Not on the UI thread.</summary>
    public static event Action<string> ImageReady;

    /// <summary>Set from config.json ("EnableImages": 0 turns off all network access).</summary>
    public static bool Enabled { get; set; } = true;

    private sealed class MapFile
    {
        [JsonPropertyName("entries")]
        public Dictionary<string, ImageEntry> Entries { get; set; }
    }

    private static Dictionary<string, ImageEntry> Map
    {
        get
        {
            if (_map != null)
            {
                return _map;
            }

            lock (Gate)
            {
                if (_map != null)
                {
                    return _map;
                }

                try
                {
                    using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);

                    if (stream == null)
                    {
                        _map = new Dictionary<string, ImageEntry>();
                    }
                    else
                    {
                        var parsed = JsonSerializer.Deserialize<MapFile>(stream);
                        _map = parsed?.Entries != null
                            ? new Dictionary<string, ImageEntry>(parsed.Entries, StringComparer.OrdinalIgnoreCase)
                            : new Dictionary<string, ImageEntry>();
                    }
                }
                catch (Exception)
                {
                    _map = new Dictionary<string, ImageEntry>();
                }

                return _map;
            }
        }
    }

    /// <summary>Does this id have a picture at all? Cheap; no I/O.</summary>
    public static bool TryGetEntry(string id, out ImageEntry entry)
    {
        entry = null;
        return !string.IsNullOrEmpty(id) && Map.TryGetValue(id, out entry);
    }

    /// <summary>
    /// The image if it is already decoded, otherwise null. Never blocks; call
    /// <see cref="Request"/> to start a load and listen for <see cref="ImageReady"/>.
    /// </summary>
    public static Image Peek(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        lock (Gate)
        {
            if (!Memory.TryGetValue(id, out var image))
            {
                return null;
            }

            Touch(id);
            return image;
        }
    }

    /// <summary>
    /// Starts loading, from disk cache or the network. Safe to call repeatedly and from
    /// any thread; duplicate and previously-failed requests are dropped.
    /// </summary>
    public static void Request(string id)
    {
        if (!TryGetEntry(id, out var entry))
        {
            return;
        }

        lock (Gate)
        {
            if (Memory.ContainsKey(id) || Failed.Contains(id) || !InFlight.Add(id))
            {
                return;
            }
        }

        _ = Task.Run(() => Load(id, entry));
    }

    private static async Task Load(string id, ImageEntry entry)
    {
        try
        {
            var bytes = ReadCache(entry.Url);

            if (bytes == null)
            {
                if (!Enabled)
                {
                    Fail(id);
                    return;
                }

                bytes = await Download(entry.Url).ConfigureAwait(false);

                if (bytes != null)
                {
                    WriteCache(entry.Url, bytes);
                }
            }

            if (bytes == null)
            {
                Fail(id);
                return;
            }

            var image = Decode(bytes);

            if (image == null)
            {
                Fail(id);
                return;
            }

            lock (Gate)
            {
                Memory[id] = image;
                Touch(id);
                Evict();
                InFlight.Remove(id);
            }

            ImageReady?.Invoke(id);
        }
        catch (Exception)
        {
            Fail(id);
        }
    }

    private static void Fail(string id)
    {
        lock (Gate)
        {
            Failed.Add(id);
            InFlight.Remove(id);
        }
    }

    // ---- memory ----------------------------------------------------------------

    private static void Touch(string id)
    {
        Recent.Remove(id);
        Recent.AddFirst(id);
    }

    private static void Evict()
    {
        while (Recent.Count > MemoryLimit)
        {
            var oldest = Recent.Last;

            if (oldest == null)
            {
                return;
            }

            Recent.RemoveLast();

            if (Memory.Remove(oldest.Value, out var image))
            {
                image.Dispose();
            }
        }
    }

    // ---- disk ------------------------------------------------------------------

    private static string CacheDir
    {
        get
        {
            if (_cacheDir != null)
            {
                return _cacheDir;
            }

            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CP2077SaveEditor", "images");

                Directory.CreateDirectory(dir);
                _cacheDir = dir;
            }
            catch (Exception)
            {
                _cacheDir = string.Empty;
            }

            return _cacheDir;
        }
    }

    private static string CachePath(string url)
    {
        if (string.IsNullOrEmpty(CacheDir))
        {
            return null;
        }

        var name = url.Split('/').LastOrDefault() ?? "image";

        foreach (var bad in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(bad, '_');
        }

        if (name.Length > 120)
        {
            name = name[^120..];
        }

        return Path.Combine(CacheDir, name);
    }

    private static byte[] ReadCache(string url)
    {
        try
        {
            var path = CachePath(url);
            return path != null && File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void WriteCache(string url, byte[] bytes)
    {
        try
        {
            var path = CachePath(url);

            if (path != null)
            {
                File.WriteAllBytes(path, bytes);
            }
        }
        catch (Exception)
        {
            // A missing cache only costs a re-download next time.
        }
    }

    // ---- network ---------------------------------------------------------------

    private static HttpClient Http
    {
        get
        {
            if (_http != null)
            {
                return _http;
            }

            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

            // Identify the tool honestly, and send the Referer the image host expects --
            // without it the CDN answers with a bot challenge instead of the picture.
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "CP2077SaveEditor/0.28 (+https://github.com/exexexexe/CyberCAT-SimpleGUI-2.31)");
            client.DefaultRequestHeaders.Referrer = new Uri("https://cyberpunk.fandom.com/");
            client.DefaultRequestHeaders.Accept.ParseAdd("image/png,image/jpeg");

            _http = client;
            return _http;
        }
    }

    /// <summary>
    /// The stored url is the file's page on the CDN, which by default content-negotiates
    /// to WebP -- a format System.Drawing cannot decode, so every image would arrive and
    /// then fail. "format=original" forces real PNG bytes, and asking the server to scale
    /// first turns a 1.7 MB render into ~190 KB.
    /// </summary>
    private static string FetchUrl(string url)
    {
        return url + "/revision/latest/scale-to-width-down/" + MaxWidth + "?format=original";
    }

    private static async Task<byte[]> Download(string url)
    {
        try
        {
            using var response = await Http.GetAsync(FetchUrl(url)).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ---- decoding --------------------------------------------------------------

    /// <summary>Decodes and shrinks. The source renders are far larger than any pane here.</summary>
    private static Image Decode(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            using var original = Image.FromStream(stream);

            var width = original.Width;
            var height = original.Height;

            if (width <= MaxWidth)
            {
                // Copy out of the stream-backed image so the MemoryStream can go.
                return new Bitmap(original);
            }

            var scaled = new Size(MaxWidth, Math.Max(1, (int)(height * (MaxWidth / (double)width))));
            var bitmap = new Bitmap(scaled.Width, scaled.Height);

            using (var g = Graphics.FromImage(bitmap))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(original, 0, 0, scaled.Width, scaled.Height);
            }

            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
