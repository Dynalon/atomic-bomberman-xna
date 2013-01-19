﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bomberman.Game.Elements.Fields;

namespace Bomberman.Game.Elements.Cells
{
    public class BlankCell : FieldCell
    {
        public BlankCell(int x, int y)
            : base(x, y)
        {
        }

        public override bool IsEmpty()
        {
            return true;
        }
    }
}