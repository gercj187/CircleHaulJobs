using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace CircleHauls
{
    /// <summary>
    /// Kombi-Lizenzicon "DVHR S|L|F" (Rangieren + Logistik + Güterverkehr) für das Deckblatt der Zugumläufe.
    /// Liegt als rohe RGBA-Pixel (256×256, unten → oben) in der DLL – braucht kein ImageConversion-Modul.
    /// </summary>
    internal static class CombinedLicenseIcon
    {
        private const int Size = 256;
        private const string ResourceName = "CircleHauls.license_dvhr_slf.rgba";
        private static Sprite _sprite;
        private static bool _tried;

        public static Sprite Sprite
        {
            get
            {
                if (_sprite != null || _tried) return _sprite;
                _tried = true;
                try
                {
                    using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
                    {
                        if (s == null) { CircleHaulsMain.Warning("Kombi-Lizenzicon fehlt in der DLL"); return null; }
                        var bytes = new byte[s.Length];
                        int read = 0;
                        while (read < bytes.Length)
                        {
                            int n = s.Read(bytes, read, bytes.Length - read);
                            if (n <= 0) break;
                            read += n;
                        }
                        if (read != Size * Size * 4) { CircleHaulsMain.Warning("Kombi-Lizenzicon hat falsche Größe"); return null; }

                        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
                        {
                            name = "CircleHauls_DVHR_SLF",
                            filterMode = FilterMode.Bilinear,
                            wrapMode = TextureWrapMode.Clamp,
                        };
                        tex.LoadRawTextureData(bytes);
                        tex.Apply(false, true);
                        UnityEngine.Object.DontDestroyOnLoad(tex);
                        _sprite = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
                        _sprite.name = "CircleHauls_DVHR_SLF";
                        UnityEngine.Object.DontDestroyOnLoad(_sprite);
                    }
                }
                catch (Exception ex)
                {
                    CircleHaulsMain.Warning("Kombi-Lizenzicon konnte nicht geladen werden: " + ex.Message);
                }
                return _sprite;
            }
        }
    }
}
