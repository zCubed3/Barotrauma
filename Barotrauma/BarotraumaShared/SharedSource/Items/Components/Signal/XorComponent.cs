﻿using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
	public class XorComponent : AndComponent
    {
        public XorComponent(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            int sendOutput = 0;
            for (int i = 0; i < timeSinceReceived.Length; i++)
            {
                if (timeSinceReceived[i] <= timeFrame) sendOutput += 1;
                timeSinceReceived[i] += deltaTime;
            }

            string signalOut = sendOutput == 1 ? output : falseOutput;
            if (string.IsNullOrEmpty(signalOut)) return;

            item.SendSignal(0, signalOut, "signal_out", null);
        }
    }
}
