﻿// Copyright 2017 Yurio Miyazawa (a.k.a zawawa) <me@yurio.net>
//
// This file is part of Gateless Gate Sharp.
//
// Gateless Gate Sharp is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Gateless Gate Sharp is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with Gateless Gate Sharp.  If not, see <http://www.gnu.org/licenses/>.



using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Cloo;



namespace GatelessGateSharp
{
    class Miner
    {
        private int mDeviceIndex;
        private bool mStopped = false;
        protected double mSpeed = 0;
        private String mAlgorithmName = "";

        public int DeviceIndex { get { return mDeviceIndex; } }
        public bool Stopped { get { return mStopped; } }
        public double Speed { get { return mSpeed; } }
        public String AlgorithmName { get { return mAlgorithmName; } }

        protected Miner(int aDeviceIndex, String aAlgorithmName )
        {
            mDeviceIndex = aDeviceIndex;
            mAlgorithmName = aAlgorithmName;
        }

        public void Stop()
        {
            mStopped = true;
        }
    }
}
