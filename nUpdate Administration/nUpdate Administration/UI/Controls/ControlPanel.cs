﻿// Author: Dominic Beger (Trade/ProgTrade)
// License: Creative Commons Attribution NoDerivs (CC-ND)
// Created: 01-08-2014 12:11
using System.Drawing;
using System.Windows.Forms;

namespace nUpdate.Administration.UI.Controls
{
    public sealed class ControlPanel : Panel
    {
        public ControlPanel()
        {
            Paint += BottomPanel_Paint;
            BackColor = SystemColors.Control;
        }

        private void BottomPanel_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.DrawLine(new Pen(Brushes.LightGray), 0, 1, Width, 1);
        }
    }
}