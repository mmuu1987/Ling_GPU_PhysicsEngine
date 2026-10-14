using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace MassEngine.Game.Ui
{
    [UxmlElement]
    public partial class TacSlider : VisualElement
    {
        float m_Value = 0.5f;
        readonly VisualElement fill, thumb;

        [UxmlAttribute] public float low { get; set; } = 0f;
        [UxmlAttribute] public float high { get; set; } = 1f;
        [UxmlAttribute] public float value { get => m_Value; set { m_Value = value; Refresh(); } }
        public event Action<float> changed;
        public event Action released;

        public TacSlider()
        {
            AddToClassList("tslider");
            var track = new VisualElement { pickingMode = PickingMode.Ignore };
            track.AddToClassList("tslider__track");
            fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.AddToClassList("tslider__fill");
            thumb = new VisualElement { pickingMode = PickingMode.Ignore };
            thumb.AddToClassList("tslider__thumb");
            track.Add(fill);
            Add(track);
            Add(thumb);
            RegisterCallback<PointerDownEvent>(e => { this.CapturePointer(e.pointerId); SetFromX(e.localPosition.x); e.StopPropagation(); });
            RegisterCallback<PointerMoveEvent>(e => { if (this.HasPointerCapture(e.pointerId)) SetFromX(e.localPosition.x); });
            RegisterCallback<PointerUpEvent>(e => { if (this.HasPointerCapture(e.pointerId)) { this.ReleasePointer(e.pointerId); released?.Invoke(); } });
            RegisterCallback<GeometryChangedEvent>(_ => Refresh());
        }

        void SetFromX(float x)
        {
            float w = Mathf.Max(1f, layout.width);
            float t = Mathf.Clamp01(x / w);
            m_Value = Mathf.Lerp(low, high, t);
            Refresh();
            changed?.Invoke(m_Value);
        }

        public void SetValueWithoutNotify(float v) { m_Value = v; Refresh(); }

        void Refresh()
        {
            float t = Mathf.Approximately(high, low) ? 0f : Mathf.Clamp01((m_Value - low) / (high - low));
            fill.style.width = Length.Percent(t * 100f);
            thumb.style.left = Length.Percent(t * 100f);
        }
    }

    [UxmlElement]
    public partial class TacToggle : VisualElement
    {
        bool m_Value;
        [UxmlAttribute] public bool value { get => m_Value; set { m_Value = value; EnableInClassList("ttoggle--on", value); } }
        public event Action<bool> changed;

        public TacToggle()
        {
            AddToClassList("ttoggle");
            var knob = new VisualElement { pickingMode = PickingMode.Ignore };
            knob.AddToClassList("ttoggle__knob");
            Add(knob);
            RegisterCallback<ClickEvent>(_ => { value = !value; changed?.Invoke(m_Value); });
        }
    }

    [UxmlElement]
    public partial class TacSeg : VisualElement
    {
        string m_Options = "";
        int m_Index;
        [UxmlAttribute] public string options { get => m_Options; set { m_Options = value ?? ""; Rebuild(); } }
        [UxmlAttribute] public int index { get => m_Index; set { m_Index = value; RefreshSel(); } }
        public event Action<int> changed;

        public TacSeg() { AddToClassList("tseg"); }

        void Rebuild()
        {
            Clear();
            var parts = m_Options.Split('|');
            for (int i = 0; i < parts.Length; i++)
            {
                int k = i;
                var b = new Button { text = parts[i] };
                b.AddToClassList("tseg__item");
                if (i == 0) b.AddToClassList("tseg__item--first");
                b.clicked += () => { index = k; changed?.Invoke(k); };
                Add(b);
            }
            RefreshSel();
        }

        void RefreshSel()
        {
            for (int i = 0; i < childCount; i++) this[i].EnableInClassList("tseg__item--on", i == m_Index);
        }
    }

    [UxmlElement]
    public partial class TacCycler : VisualElement
    {
        string m_Options = "";
        int m_Index;
        string[] parts = new string[0];
        readonly Label value;
        readonly VisualElement dots;
        [UxmlAttribute] public string options { get => m_Options; set { m_Options = value ?? ""; parts = m_Options.Split('|'); Refresh(); } }
        [UxmlAttribute] public int index { get => m_Index; set { m_Index = value; Refresh(); } }
        public event Action<int> changed;

        public TacCycler()
        {
            AddToClassList("tcycler");
            var l = new Button { text = "‹" }; l.AddToClassList("tcycler__btn");
            var r = new Button { text = "›" }; r.AddToClassList("tcycler__btn");
            value = new Label(); value.AddToClassList("tcycler__value");
            dots = new VisualElement { pickingMode = PickingMode.Ignore }; dots.AddToClassList("tcycler__dots");
            l.clicked += () => Step(-1);
            r.clicked += () => Step(1);
            Add(l); Add(value); Add(r); Add(dots);
        }

        void Step(int d)
        {
            if (parts.Length == 0) return;
            index = (m_Index + d + parts.Length) % parts.Length;
            changed?.Invoke(m_Index);
        }

        void Refresh()
        {
            if (value == null) return;
            value.text = parts.Length > 0 ? parts[Mathf.Clamp(m_Index, 0, parts.Length - 1)] : "";
            dots.Clear();
            for (int i = 0; i < parts.Length; i++)
            {
                var d = new VisualElement();
                d.AddToClassList("tcycler__dot");
                d.EnableInClassList("tcycler__dot--on", i == m_Index);
                dots.Add(d);
            }
        }
    }
}
