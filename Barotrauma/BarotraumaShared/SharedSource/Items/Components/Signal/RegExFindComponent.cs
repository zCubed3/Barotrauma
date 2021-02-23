﻿using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
	public class RegExFindComponent : ItemComponent
    {
        private string expression;

        private string receivedSignal;
        private string previousReceivedSignal;

        private bool previousResult;
        private GroupCollection previousGroups;

        private Regex regex;

        private bool nonContinuousOutputSent;

        [InGameEditable, Serialize("1", true, description: "The signal this item outputs when the received signal matches the regular expression.", alwaysUseInstanceValues: true)]
        public string Output { get; set; }

        [InGameEditable, Serialize(false, true, description: "Should the component output a value of a capture group instead of a constant signal.", alwaysUseInstanceValues: true)]
        public bool UseCaptureGroup { get; set; }

        [InGameEditable, Serialize("0", true, description: "The signal this item outputs when the received signal does not match the regular expression.", alwaysUseInstanceValues: true)]
        public string FalseOutput { get; set; }

        [InGameEditable, Serialize(true, true, description: "Should the component keep sending the output even after it stops receiving a signal, or only send an output when it receives a signal.", alwaysUseInstanceValues: true)]
        public bool ContinuousOutput { get; set; }

        [InGameEditable, Serialize("", true, description: "The regular expression used to check the incoming signals.", alwaysUseInstanceValues: true)]
        public string Expression
        {
            get { return expression; }
            set 
            {
                if (expression == value) return;
                expression = value;
                previousReceivedSignal = "";

                try
                {
                    regex = new Regex(@expression);
                }

                catch
                {
                    item.SendSignal(0, "ERROR", "signal_out", null);
                    return;
                }
            }
        }

        public RegExFindComponent(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (string.IsNullOrWhiteSpace(expression) || regex == null) return;

            if (receivedSignal != previousReceivedSignal && receivedSignal != null)
            {
                try
                {
                    Match match = regex.Match(receivedSignal);
                    previousResult =  match.Success;
                    previousGroups = UseCaptureGroup && previousResult ? match.Groups : null;
                    previousReceivedSignal = receivedSignal;

                }
                catch
                {
                    item.SendSignal(0, "ERROR", "signal_out", null);
                    previousResult = false;
                    return;
                }
            }

            string signalOut;
            if (previousResult)
            {
                if (UseCaptureGroup)
                {
                    if (previousGroups != null && previousGroups.TryGetValue(Output, out Group group))
                    {
                        signalOut = group.Value;
                    }
                    else
                    {
                        signalOut = FalseOutput;
                    }
                }
                else
                {
                    signalOut = Output;
                }
            }
            else
            {
                signalOut = FalseOutput;
            }

            if (ContinuousOutput)
            {
                if (!string.IsNullOrEmpty(signalOut)) { item.SendSignal(0, signalOut, "signal_out", null); }
            }
            else if (!nonContinuousOutputSent)
            {
                if (!string.IsNullOrEmpty(signalOut)) { item.SendSignal(0, signalOut, "signal_out", null); }
                nonContinuousOutputSent = true;
            }
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f, float signalStrength = 1.0f)
        {
            switch (connection.Name)
            {
                case "signal_in":
                    receivedSignal = signal;
                    nonContinuousOutputSent = false;
                    break;
                case "set_output":
                    Output = signal;
                    break;
            }
        }
    }
}
