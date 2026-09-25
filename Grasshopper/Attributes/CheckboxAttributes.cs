using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GH_IO;
using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;

namespace Ant.GrasshopperPlugIn.Attributes
{
    public sealed class CheckboxAttributeItem : GH_ISerializable
    {
        internal Rectangle Bounds;
        internal Rectangle TextBounds;

        public string Title { get; set; }
        public bool State { get; set; }

        public CheckboxAttributeItem(string title, bool state)
        {
            Title = title;
            State = state;
        }

        public bool Write(GH_IWriter writer)
        {
            writer.SetString("title", Title);
            writer.SetBoolean("state", State);

            return true;
        }

        public bool Read(GH_IReader reader)
        {
            Title = reader.GetString("title");
            State = reader.GetBoolean("state");

            return true;
        }
    }

    public sealed class CheckboxAttributes : GH_ComponentAttributes
    {
        public readonly CheckboxAttributeItem[] Items;
        private readonly Action<CheckboxAttributes, int> _callback;

        public CheckboxAttributes(IGH_Component owner, Action<CheckboxAttributes, int> callback, params CheckboxAttributeItem[] items)
            : base(component: owner)
        {
            _callback = callback;
            Items = items;
        }

        #region IO
        public override bool Write(GH_IWriter writer)
        {
            bool success = base.Write(writer); ;

            GH_IWriter w = writer.CreateChunk("checkboxAttribute");
            if (w != null)
            {
                int n = 0;
                foreach (CheckboxAttributeItem i in Items)
                {
                    GH_IWriter itemWriter = w.CreateChunk($"Item_#{n}");
                    i.Write(itemWriter);
                    n = n + 1;
                }
            }

            return success;
        }

        public override bool Read(GH_IReader reader)
        {
            bool success = base.Read(reader);

            GH_IReader r = reader.FindChunk("checkboxAttribute");
            if (r != null)
            {
                int n = 0;
                foreach (CheckboxAttributeItem i in Items)
                {
                    GH_IReader itemReader = r.FindChunk($"Item_#{n}");
                    i.Read(itemReader);
                    n = n + 1;
                }
            }

            return success;
        }
        #endregion

        protected override void Layout()
        {
            base.Layout();

            int n = Items.Length;

            const int h = 10;
            const int s = 2;

            Rectangle comp = GH_Convert.ToRectangle(Bounds);
            int k = comp.Bottom;
            int w = comp.Width;

            comp.Height += h * n + s * n;
            Bounds = comp;

            for (int i = 0; i < n; i++)
            {
                Rectangle c = comp;

                c.Y = k + h * i + s * i;
                c.X = c.Left + s;

                c.Height = h;
                c.Width = h;

                Items[i].Bounds = c;


                Rectangle t = comp;

                t.Y = k + h * i + s * i;
                t.X = t.Left + s + h + s;

                t.Height = h;
                t.Width = w - s - s - h;

                Items[i].TextBounds = t;
            }
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            if (channel == GH_CanvasChannel.Objects)
            {
                int i = 0;
                foreach (CheckboxAttributeItem rec in Items)
                {
                    var checkbox = GH_Capsule.CreateTextCapsule(rec.Bounds, rec.TextBounds, rec.State ? GH_Palette.Hidden : GH_Palette.Normal, rec.Title, 0, 0);

                    checkbox.TextOrientation = GH_Orientation.horizontal_near;
                    checkbox.Font = new Font(checkbox.Font.FontFamily, 5, rec.State ? FontStyle.Bold : FontStyle.Regular);

                    checkbox.Render(graphics, false, Owner.Locked, false);
                    checkbox.Dispose();

                    i++;
                }
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (!Owner.Locked && e.Clicks == 1 && e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                int i = 0;
                foreach (CheckboxAttributeItem rec in Items)
                {
                    if (((RectangleF)rec.Bounds).Contains(e.CanvasLocation))
                        _callback.Invoke(this, i);

                    i++;
                }
            }

            return base.RespondToMouseDown(sender, e);
        }
    }
}
