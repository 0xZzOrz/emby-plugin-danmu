using System;
using MediaBrowser.Controller.Entities;

namespace Emby.Plugin.Danmu.Model
{
    public class LibraryEvent : IEquatable<LibraryEvent>
    {
        public BaseItem Item { get; set; }

        public EventType EventType { get; set; }

        public bool Equals(LibraryEvent other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Item?.Id == other.Item?.Id && EventType == other.EventType;
        }

        public override bool Equals(object obj)
        {
            return obj is LibraryEvent other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Item?.Id, EventType);
        }
    }
}

