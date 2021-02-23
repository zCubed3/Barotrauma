﻿using System.Collections.Generic;

namespace Barotrauma
{
    public partial class TraitorMissionResult
    {
        public readonly string EndMessage;

        public readonly string MissionIdentifier;

        public readonly bool Success;

        public readonly List<Character> Characters = new List<Character>();
    }
}
