﻿using System;
using System.Globalization;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
	public class FunctionComponent : ItemComponent
    {
        public enum FunctionType
        {
            Round,
            Ceil,
            Floor,
            Factorial,
            AbsoluteValue,
            SquareRoot
        }

        [Serialize(FunctionType.Round, false, description: "Which kind of function to run the input through.", alwaysUseInstanceValues: true)]
        public FunctionType Function
        {
            get; set;
        }

        public FunctionComponent(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0, float signalStrength = 1)
        {
            if (connection.Name != "signal_in") return;
            if (!float.TryParse(signal, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) return;
            switch (Function)
            {
                case FunctionType.Round:
                    item.SendSignal(0, Math.Round(value).ToString("G", CultureInfo.InvariantCulture), "signal_out", null);
                    break;
                case FunctionType.Ceil:
                    item.SendSignal(0, Math.Ceiling(value).ToString("G", CultureInfo.InvariantCulture), "signal_out", null);
                    break;
                case FunctionType.Floor:
                    item.SendSignal(0, Math.Floor(value).ToString("G", CultureInfo.InvariantCulture), "signal_out", null);
                    break;
                case FunctionType.Factorial:
                    int intVal = (int)Math.Min(value, 20);
                    ulong factorial = 1;
                    for (int i = intVal; i > 0; i--)
                    {
                        factorial *= (ulong)i;
                    }
                    item.SendSignal(0, factorial.ToString(), "signal_out", null);
                    break;
                case FunctionType.AbsoluteValue:
                    item.SendSignal(0, Math.Abs(value).ToString("G", CultureInfo.InvariantCulture), "signal_out", null);
                    break;
                case FunctionType.SquareRoot:
                    if (value > 0)
                    {
                        item.SendSignal(0, Math.Sqrt(value).ToString("G", CultureInfo.InvariantCulture), "signal_out", null);
                    }
                    break;
                default:
                    throw new NotImplementedException($"Function {Function} has not been implemented.");
            }
        }
    }
}
