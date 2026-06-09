using System;
using System.IO;
using UnityEngine;
using Eliminated.Game.Accessibility;

namespace Eliminated.Game.Save
{
    /// <summary>
    /// Loads/saves the local <see cref="PlayerProfile"/> as JSON and applies
    /// settings (colorblind palette, audio volumes) on load. Single owner of the
    /// save file so persistence stays in one place (Steam/UGS cloud sync hooks
    /// onto this in later phases).
    /// </summary>
    public static class SaveService
    {
        private const string FileName = "profile.json";

        private static string Path => System.IO.Path.Combine(Application.persistentDataPath, FileName);

        public static PlayerProfile Current { get; private set; }

        public static PlayerProfile Load()
        {
            try
            {
                if (File.Exists(Path))
                {
                    string json = File.ReadAllText(Path);
                    Current = JsonUtility.FromJson<PlayerProfile>(json) ?? new PlayerProfile();
                }
                else
                {
                    Current = new PlayerProfile();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Eliminated] Failed to load profile, using defaults: {e.Message}");
                Current = new PlayerProfile();
            }
            ApplySettings(Current.settings);
            return Current;
        }

        public static void Save()
        {
            if (Current == null) return;
            try
            {
                File.WriteAllText(Path, JsonUtility.ToJson(Current, prettyPrint: true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Eliminated] Failed to save profile: {e.Message}");
            }
        }

        /// <summary>Apply settings to the live systems they control.</summary>
        public static void ApplySettings(GameSettings s)
        {
            if (s == null) return;
            Palette.Mode = s.colorblind;
            AudioListener.volume = Mathf.Clamp01(s.masterVolume);
        }
    }
}
