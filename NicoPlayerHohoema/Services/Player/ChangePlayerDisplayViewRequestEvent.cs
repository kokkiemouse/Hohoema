﻿using NicoPlayerHohoema.Models;
using Prism.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NicoPlayerHohoema.Services.Player
{
    public sealed class ChangePlayerDisplayViewRequestEvent : PubSubEvent<PlayerDisplayMode>
    {
    }
}
