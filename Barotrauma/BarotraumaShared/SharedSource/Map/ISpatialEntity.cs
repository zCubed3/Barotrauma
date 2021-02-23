using Microsoft.Xna.Framework;

namespace Barotrauma
{
    public interface ISpatialEntity
    {
        Vector2 Position { get; }
        Vector2 WorldPosition { get; }
        Vector2 SimPosition { get; }
        Submarine Submarine { get; }
    }

    public interface IIgnorable : ISpatialEntity
    {
        bool IgnoreByAI { get; }
        bool OrderedToBeIgnored { get; set; }
    }
}
