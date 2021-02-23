﻿using System.Globalization;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
	public class ExponentiationComponent : ItemComponent
    {
        private float exponent;
        [InGameEditable, Serialize(1.0f, false, description: "The exponent of the operation.", alwaysUseInstanceValues: true)]
        public float Exponent
        {
            get
            {
                return exponent;
            }
            set
            {
                exponent = value;
            }
        }

        public ExponentiationComponent(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0, float signalStrength = 1)
        {
            switch (connection.Name)
            {
                case "set_exponent":
                case "exponent":
                    float.TryParse(signal, NumberStyles.Float, CultureInfo.InvariantCulture, out exponent);
                    break;
                case "signal_in":
                    float.TryParse(signal, NumberStyles.Float, CultureInfo.InvariantCulture, out float value);
                    item.SendSignal(0, MathUtils.Pow(value, Exponent).ToString("G", CultureInfo.InvariantCulture), "signal_out", null);
                    break;
            }
        }
    }
}