using System;
using Eliminated.Sim.Model;

namespace Eliminated.Sim.Games
{
    /// <summary>
    /// Maps a <see cref="GameId"/> to its per-tick snapshot <c>Data</c> payload
    /// type. Single source of truth so the online client (which deserializes the
    /// data json with JsonUtility) and the verification tests (System.Text.Json)
    /// agree on which type each game's <c>Snapshot.Data</c> deserializes to.
    /// </summary>
    public static class DataWire
    {
        public static Type TypeFor(GameId id)
        {
            switch (id)
            {
                case GameId.RedLight: return typeof(RedLightGreenLight.RlglData);
                case GameId.Tag: return typeof(Tag.TagData);
                case GameId.Mingle: return typeof(Mingle.MingleData);
                case GameId.GlassBridge: return typeof(GlassBridge.GlassData);
                case GameId.TugOfWar: return typeof(TugOfWar.TugData);
                case GameId.RpsMinusOne: return typeof(RpsMinusOne.RpsData);
                case GameId.JumpRope: return typeof(JumpRope.RopeData);
                case GameId.Boomerang: return typeof(Boomerang.BoomData);
                case GameId.Dodgeball: return typeof(Dodgeball.DodgeData);
                case GameId.MusicalChairs: return typeof(MusicalChairs.McData);
                case GameId.PresentSwap: return typeof(PresentSwap.PresentData);
                case GameId.PropHunt: return typeof(PropHunt.PropData);
                case GameId.ChutesAndLadders: return typeof(ChutesAndLadders.ChutesData);
                case GameId.SimonSays: return typeof(SimonSays.SimonData);
                case GameId.KeepyUppy: return typeof(KeepyUppy.KeepyData);
                case GameId.KingOfTheHill: return typeof(KingOfTheHill.KothData);
                default: return null;
            }
        }
    }
}
