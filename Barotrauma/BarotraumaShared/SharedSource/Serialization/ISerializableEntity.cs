using System.Collections.Generic;

namespace Barotrauma
{
    public interface ISerializableEntity
    {
        string Name
        {
            get;
        }

        Dictionary<string, SerializableProperty> SerializableProperties
        {
            get;
        }
    }
}
