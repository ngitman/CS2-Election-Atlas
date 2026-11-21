using System;

namespace ElectionAtlas.Model
{
    public sealed class Party
    {
        public Party(string id, string name, uint color, Position position)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("A party needs an id.", nameof(id));

            Id = id;
            Name = string.IsNullOrEmpty(name) ? id : name;
            Color = color;
            Position = position;
        }

        /// <summary>Stable identifier, kept in saves. Never shown to the player.</summary>
        public string Id { get; }

        public string Name { get; }

        /// <summary>0xRRGGBB.</summary>
        public uint Color { get; }

        public Position Position { get; }

        public override string ToString() => $"{Name} {Position}";
    }
}
