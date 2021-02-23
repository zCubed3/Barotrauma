﻿using System.Globalization;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
	public class ModuloComponent : ItemComponent
    {
        private float modulus;
        [InGameEditable, Serialize(1.0f, false, description: "The modulus of the operation. Must be non-zero.", alwaysUseInstanceValues: true)]
        public float Modulus
        {
            get { return modulus; }
            set
            {
                modulus = MathUtils.NearlyEqual(value, 0.0f) ? 1.0f : value; 
            }
        }

        public ModuloComponent(Item item, XElement element) : base(item, element)
        {
            IsActive = true;
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0, float signalStrength = 1)
        {
            switch (connection.Name)
            {
                case "set_modulus":
                case "modulus":
                    float.TryParse(signal, NumberStyles.Float, CultureInfo.InvariantCulture, out float newModulus);
                    Modulus = newModulus;
                    break;
                case "signal_in":
                    float.TryParse(signal, NumberStyles.Float, CultureInfo.InvariantCulture, out float value);
                    item.SendSignal(0, (value % modulus).ToString("G", CultureInfo.InvariantCulture), "signal_out", null);
                    break;
            }
                
        }
    }
}
