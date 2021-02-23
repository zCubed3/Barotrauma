﻿using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
	public class AdderComponent : ArithmeticComponent
    {
        public AdderComponent(Item item, XElement element)
            : base(item, element)
        {
        }

        protected override float Calculate(float signal1, float signal2)
        {
            return signal1 + signal2;
        }
    }
}
