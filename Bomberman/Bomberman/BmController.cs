﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BomberEngine.Game;
using Bomberman.Multiplayer;
using BomberEngine.Core.Input;
using BomberEngine.Core.Events;
using Bomberman.Networking;

namespace Bomberman
{
    public abstract class BmController : Controller
    {
        protected override void OnStop()
        {
            UnregisterNotifications();
        }

        //////////////////////////////////////////////////////////////////////////////

        #region Helpers

        protected new BmRootController GetRootController()
        {
            return base.GetRootController() as BmRootController;
        }

        protected NetworkManager GetNetwork()
        {
            return GetRootController().GetNetwork();
        }

        protected BmAssetManager Assets()
        {
            return BmApplication.Assets();
        }

        protected InputManager Input()
        {
            return BmApplication.Input();
        }

        #endregion
    }
}
