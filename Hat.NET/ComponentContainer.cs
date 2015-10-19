﻿/*
This class is part of modification of TerrariaServerApi
Copyright (C) 2011-2015 Nyx Studios (fka. The TShock Team)

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.
This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.
You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/
using System;

namespace Hat.NET
{
    public class ComponentContainer : IDisposable
    {
        public HatComponent Component
        {
            get;
            protected set;
        }

        public bool Initialized
        {
            get;
            protected set;
        }
        public bool Dll
        {
            get;
            set;
        }

        public ComponentContainer(HatComponent extension)
            : this(extension, true)
        {
        }

        public ComponentContainer(HatComponent plugin, bool dll)
        {
            this.Component = plugin;
            this.Initialized = false;
            this.Dll = dll;
        }

        public void Initialize()
        {
            this.Component.Initialize();
            this.Initialized = true;
        }

        public void DeInitialize()
        {
            this.Initialized = false;
        }

        public void Dispose()
        {
            this.Component.Dispose();
        }
    }
}

