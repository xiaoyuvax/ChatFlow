using System.Drawing;
using System.Drawing.Drawing2D;

namespace ChatFlow
{
    /// <summary>Loop 嵌入对话流的一张调查卡：可折叠、按状态着色、可承载多种控件。</summary>
    public sealed class ChatSurvey
    {
        public string Id { get; set; } = "";
        public int AnchorTurnIndex { get; set; } = -1;
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
        public string AccentHex { get; set; } = "#722ED1";
        public string State { get; set; } = "pending";
        public string AnswerSummary { get; set; }
        public List<SurveyField> Fields { get; set; } = [];

        public Color Accent
        {
            get
            {
                try { return ColorTranslator.FromHtml(AccentHex); }
                catch { return Color.FromArgb(114, 46, 209); }
            }
        }
    }

    public sealed class SurveyField
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        /// <summary>choice | multi | text | scale</summary>
        public string Kind { get; set; } = "choice";
        public List<string> Options { get; set; } = [];
        public string Placeholder { get; set; } = "";
    }

    /// <summary>调查卡行：折叠时只画标题栏；展开时铺控件；提交/跳过后自动折叠。</summary>
    public sealed class ChatCardRow : Control
    {
        private const int Pad = 12;

        private readonly Label _title = new();
        private readonly Label _summary = new();
        private readonly Label _body = new();
        private readonly FlowLayoutPanel _fields = new();
        private readonly FlowLayoutPanel _buttons = new();
        private readonly Button _submit = new();
        private readonly Button _skip = new();
        private readonly List<(SurveyField Field, Func<string> Read)> _readers = [];

        private ChatSurvey _survey;
        private string _boundKey;
        private bool _expanded;
        private bool _indexing;

        public event Action<ChatSurvey, Dictionary<string, string>> Submitted;
        public event Action<ChatSurvey> Skipped;
        public event Action<ChatSurvey, bool> Toggled;

        public ChatCardRow()
        {
            DoubleBuffered = true;
            foreach (var c in new Control[] { _title, _summary, _body, _fields, _buttons })
                Controls.Add(c);
            Controls.Add(_submit);
            Controls.Add(_skip);

            _title.Font = new Font("Microsoft YaHei UI", 9.75F, FontStyle.Bold);
            _summary.Font = new Font("Microsoft YaHei UI", 9F);
            _body.Font = new Font("Microsoft YaHei UI", 9F);
            _fields.FlowDirection = FlowDirection.TopDown;
            _fields.WrapContents = false;
            _fields.AutoSize = true;
            _fields.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _buttons.FlowDirection = FlowDirection.LeftToRight;
            _buttons.WrapContents = false;
            _buttons.AutoSize = true;
            _buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;

            _submit.Text = "提交";
            _submit.FlatStyle = FlatStyle.System;
            _submit.AutoSize = false;
            _skip.Text = "跳过";
            _skip.FlatStyle = FlatStyle.System;
            _skip.AutoSize = false;
            _submit.Click += (_, _) => Submit();
            _skip.Click += (_, _) => { Skipped?.Invoke(_survey); };
            _title.Cursor = Cursors.Hand;
            _title.Click += (_, _) => { _expanded = !_expanded; Toggled?.Invoke(_survey, _expanded); ApplyState(); };
            _summary.Cursor = Cursors.Hand;
            _summary.Click += (_, _) => { _expanded = !_expanded; Toggled?.Invoke(_survey, _expanded); ApplyState(); };
        }

        private int S(float v) => Math.Max(1, (int)Math.Round(v * DeviceDpi / 96.0));

        public ChatSurvey Survey => _survey;

        public bool Expanded
        {
            get => _expanded;
            set { if (_expanded == value) return; _expanded = value; ApplyState(); }
        }

        public int Bind(ChatSurvey survey, Font font, int width, bool expanded)
        {
            string key = survey?.Id + "|" + survey?.State + "|" + expanded + "|" + width;
            if (key == _boundKey && _survey != null) return Remeasure();

            _survey = survey;
            _expanded = expanded;
            _boundKey = key;
            _readers.Clear();
            _fields.Controls.Clear();

            _title.Text = survey.Title;
            _title.ForeColor = ChatTheme.Current?.ChatFore ?? Color.FromArgb(31, 31, 31);
            _body.Text = survey.Body;
            _body.ForeColor = ChatTheme.Current?.ForeDim ?? Color.FromArgb(110, 110, 110);
            _summary.Text = survey.State == "answered" ? survey.AnswerSummary ?? "已提交" : "";

            foreach (var field in survey.Fields) AddField(field);

            int inner = width - S(Pad * 2) - S(6);
            _title.MaximumSize = new Size(inner, 0);
            _body.MaximumSize = new Size(inner, 0);
            _summary.MaximumSize = new Size(inner, 0);

            ApplyState();
            return Remeasure();
        }

        private void AddField(SurveyField field)
        {
            var host = new Panel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, S(4), 0, S(2)) };
            var label = new Label
            {
                Text = field.Label,
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular),
                ForeColor = ChatTheme.Current?.ForeDim ?? Color.FromArgb(110, 110, 110)
            };
            bool hasLabel = !string.IsNullOrWhiteSpace(field.Label);
            if (hasLabel) host.Controls.Add(label);

            int top = hasLabel ? label.PreferredHeight + S(2) : 0;
            if (field.Kind == "text")
            {
                var box = new TextBox { Width = S(300), PlaceholderText = field.Placeholder, BorderStyle = BorderStyle.FixedSingle, Top = top };
                host.Controls.Add(box);
                _readers.Add((field, () => box.Text));
            }
            else if (field.Kind == "multi")
            {
                int y = top;
                var boxes = new List<CheckBox>();
                foreach (string option in field.Options)
                {
                    var box = new CheckBox { Text = option, AutoSize = true, Top = y, Left = S(4) };
                    host.Controls.Add(box);
                    boxes.Add(box);
                    y += box.PreferredSize.Height + S(2);
                }
                _readers.Add((field, () => string.Join(", ", boxes.Where(b => b.Checked).Select(b => b.Text))));
                host.Height = y;
            }
            else
            {
                int y = top;
                var radios = new List<RadioButton>();
                var options = field.Kind == "scale"
                    ? Enumerable.Range(1, Math.Max(3, field.Options.Count == 0 ? 5 : field.Options.Count)).Select(i => i.ToString()).ToList()
                    : field.Options;
                foreach (string option in options)
                {
                    var radio = new RadioButton { Text = option, AutoSize = true, Top = y, Left = S(4) };
                    host.Controls.Add(radio);
                    radios.Add(radio);
                    y += radio.PreferredSize.Height + S(2);
                }
                _readers.Add((field, () => radios.FirstOrDefault(r => r.Checked)?.Text));
                host.Height = y;
            }
            _fields.Controls.Add(host);
        }

        private void Submit()
        {
            var answers = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (field, read) in _readers)
            {
                string value = read() ?? "";
                if (!string.IsNullOrWhiteSpace(value)) answers[field.Key] = value;
            }
            Submitted?.Invoke(_survey, answers);
        }

        private void ApplyState()
        {
            bool answered = _survey?.State == "answered";
            bool showBody = _expanded && !answered;

            _body.Visible = showBody && !string.IsNullOrWhiteSpace(_survey?.Body);
            _fields.Visible = showBody && _survey?.Fields.Count > 0;
            _buttons.Visible = showBody;
            _submit.Visible = showBody;
            _skip.Visible = showBody;
            _summary.Visible = !showBody && !string.IsNullOrWhiteSpace(_summary.Text);
            _title.Text = (_expanded ? "▾ " : "▸ ") + _survey?.Title;
            Invalidate();
        }

        public int Remeasure()
        {
            int width = Width > S(40) ? Width : S(320);
            int inner = width - S(Pad * 2) - S(6);
            int h = S(Pad) + _title.PreferredHeight + S(4);

            bool answered = _survey?.State == "answered";
            bool showBody = _expanded && !answered;

            if (showBody)
            {
                if (!string.IsNullOrWhiteSpace(_survey?.Body))
                {
                    _body.MaximumSize = new Size(inner, 0);
                    h += _body.PreferredHeight + S(6);
                }
                if (_fields.Controls.Count > 0)
                {
                    int fieldsH = 0;
                    foreach (Control c in _fields.Controls) fieldsH += c.Height + c.Margin.Vertical;
                    h += fieldsH + S(4);
                }
                h += S(28) + S(4);
            }
            else if (!string.IsNullOrWhiteSpace(_summary.Text))
            {
                h += _summary.PreferredHeight + S(2);
            }

            return h + S(Pad);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int left = S(Pad);
            int width = Width - S(Pad * 2) - S(6);
            int y = S(Pad);
            _title.SetBounds(left, y, width, _title.PreferredHeight);
            y += _title.PreferredHeight + S(4);
            _body.SetBounds(left, y, width, _body.Visible ? _body.PreferredHeight : 0);
            if (_body.Visible) y += _body.PreferredHeight + S(6);
            if (_fields.Visible)
            {
                _fields.SetBounds(left, y, width, _fields.PreferredSize.Height);
                y += _fields.PreferredSize.Height + S(4);
            }
            _buttons.SetBounds(left, y, width, _buttons.PreferredSize.Height);
            _submit.SetBounds(left, y, S(72), S(28));
            _skip.SetBounds(left + S(80), y, S(72), S(28));
            _summary.SetBounds(left, y, width, _summary.Visible ? _summary.PreferredHeight : 0);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color accent = _survey?.Accent ?? Color.FromArgb(114, 46, 209);
            Color back = ChatTheme.Current?.Control ?? Color.FromArgb(249, 247, 255);
            Color border = ChatTheme.Current?.Border ?? Color.FromArgb(224, 214, 246);

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var fill = new SolidBrush(back)) g.FillRectangle(fill, rect);
            using (var pen = new Pen(border)) g.DrawRectangle(pen, rect);
            using (var bar = new SolidBrush(accent)) g.FillRectangle(bar, 0, 0, S(4), Height);
            base.OnPaint(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var f in _readers.Select(r => r.Field)) { }
            }
            base.Dispose(disposing);
        }
    }
}
