using System.Collections.Generic;

namespace Eliminated.Sim.Model
{
    /// <summary>A one-shot visual effect the sim asks the client to spawn.</summary>
    public struct Effect
    {
        public EffectKind Kind;
        public float X;
        public float Y;
        public float A;     // aux scalar (intensity / radius / angle)
        public string Tag;  // optional discriminator (e.g. powerup kind)

        public Effect(EffectKind kind, float x = 0f, float y = 0f, float a = 0f, string tag = null)
        {
            Kind = kind;
            X = x;
            Y = y;
            A = a;
            Tag = tag;
        }
    }

    /// <summary>
    /// What the client renders for one tick. For in-process play the view reads
    /// <see cref="Actors"/> directly; for online play this is serialized per
    /// client with <see cref="Secrets"/> folded into that client's view only.
    /// </summary>
    public sealed class Snapshot
    {
        public GameId Game;
        public double T;             // sim time (ms)
        public double? StartAt;      // epoch ms when input unfreezes (GO hold)
        public List<Actor> Actors;   // participants this tick
        public object Data;          // per-game payload (light phase, rope pos, …)
        public List<Effect> Fx;      // one-shot effects since last tick
        public Dictionary<string, object> Secrets; // playerId → hidden info
    }
}
