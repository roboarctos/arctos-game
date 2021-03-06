﻿using System;

namespace Arctos.Game.Model
{
    /// <summary>
    /// The Area indicates where the robot has to drive
    /// and where it already was
    /// </summary>
    [Serializable]
    public class Area
    {
        public enum AreaStatus
        {
            None,
            WronglyPassed,
            CorrectlyPassed
        }

        public int Row { get; set; }
        public int Column { get; set; }
        public AreaStatus Status { get; set; }
        public string AreaId { get; set; }
    }
}