using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using ElementCoordinateExtractor.Services;

namespace ElementCoordinateExtractor.Models
{
    public class RebarLegItem : INotifyPropertyChanged
    {
        private double _factor = 1.0;

        public int Index { get; set; }
        public string Label { get; set; } = string.Empty;
        public double LengthMm { get; set; }
        public string LengthMmFormatted => $"{LengthMm:N0} mm";

        public double Factor
        {
            get => _factor;
            set
            {
                double clamped = Math.Round(Math.Clamp(value, 0.2, 3.0), 2);
                if (Math.Abs(_factor - clamped) > 1e-4)
                {
                    _factor = clamped;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FactorDisplay));
                    FactorChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public string FactorDisplay => $"x{_factor:0.00}";

        public event EventHandler? FactorChanged;
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }

    public class RebarShapePreviewItem : INotifyPropertyChanged
    {
        private ImageSource? _previewImage;
        private string _statusText = "Chưa gán";
        private string _statusIcon = "⚪";
        private Brush _statusBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 119, 140));
        private bool _isAssigned;
        private bool _syncEqualLegs = true;
        private bool _isSyncing = false;
        private bool _isFlippedX = false;
        private bool _isFlippedY = false;
        private int _horizontalLegIndex = 0;
        private double _dimOffset = 35.0;

        public string ShapeName { get; set; } = string.Empty;
        public Element RebarElement { get; set; }
        public RebarShape? Shape { get; set; }
        public RebarShape2D Shape2D { get; set; }
        public bool HasExistingImage { get; set; }
        public string ImagePath { get; set; } = string.Empty;

        public bool IsFlippedX
        {
            get => _isFlippedX;
            set
            {
                if (_isFlippedX != value)
                {
                    _isFlippedX = value;
                    OnPropertyChanged();
                    PreviewNeedsRefresh?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public bool IsFlippedY
        {
            get => _isFlippedY;
            set
            {
                if (_isFlippedY != value)
                {
                    _isFlippedY = value;
                    OnPropertyChanged();
                    PreviewNeedsRefresh?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public int HorizontalLegIndex
        {
            get => _horizontalLegIndex;
            set
            {
                if (_horizontalLegIndex != value)
                {
                    _horizontalLegIndex = value;
                    OnPropertyChanged();
                    PreviewNeedsRefresh?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public double DimOffset
        {
            get => _dimOffset;
            set
            {
                double clamped = Math.Round(Math.Clamp(value, 15.0, 80.0));
                if (Math.Abs(_dimOffset - clamped) > 1e-4)
                {
                    _dimOffset = clamped;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DimOffsetDisplay));
                    PreviewNeedsRefresh?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public string DimOffsetDisplay => $"{_dimOffset:0} px";

        public ObservableCollection<RebarLegItem> Legs { get; } = new();

        public bool SyncEqualLegs
        {
            get => _syncEqualLegs;
            set
            {
                _syncEqualLegs = value;
                OnPropertyChanged();
            }
        }

        public ImageSource? PreviewImage
        {
            get => _previewImage;
            set
            {
                _previewImage = value;
                OnPropertyChanged();
            }
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }

        public string StatusIcon
        {
            get => _statusIcon;
            set
            {
                _statusIcon = value;
                OnPropertyChanged();
            }
        }

        public Brush StatusBrush
        {
            get => _statusBrush;
            set
            {
                _statusBrush = value;
                OnPropertyChanged();
            }
        }

        public bool IsAssigned
        {
            get => _isAssigned;
            set
            {
                _isAssigned = value;
                OnPropertyChanged();
            }
        }

        public string LegsSummary
        {
            get
            {
                if (Legs.Count == 0) return "Không có đoạn";
                if (Legs.Count == 1) return $"Đoạn: {Legs[0].Label} ({Legs[0].LengthMmFormatted})";
                return $"Đoạn: {string.Join(", ", Legs.Select(l => l.Label))}";
            }
        }

        public RebarShapePreviewItem(string shapeName, Element rebarElem, RebarShape? shape, RebarShape2D shape2D, bool hasExisting)
        {
            ShapeName = shapeName;
            RebarElement = rebarElem;
            Shape = shape;
            Shape2D = shape2D;
            HasExistingImage = hasExisting;
            _horizontalLegIndex = shape2D.DefaultHorizontalLegIndex;

            if (hasExisting)
            {
                _statusText = "Đã có ảnh cũ";
                _statusIcon = "🟡";
                _statusBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 171, 0));
            }

            foreach (var leg in shape2D.Legs)
            {
                var legItem = new RebarLegItem
                {
                    Index = leg.Index,
                    Label = leg.Label,
                    LengthMm = leg.LengthMm,
                    Factor = 1.0
                };
                legItem.FactorChanged += (s, e) => OnLegFactorChanged(legItem);
                Legs.Add(legItem);
            }
        }

        public event EventHandler? PreviewNeedsRefresh;

        private void OnLegFactorChanged(RebarLegItem changedLeg)
        {
            if (_isSyncing) return;

            if (_syncEqualLegs)
            {
                _isSyncing = true;
                try
                {
                    foreach (var other in Legs)
                    {
                        if (other != changedLeg && Math.Abs(other.LengthMm - changedLeg.LengthMm) < 1.0)
                        {
                            other.Factor = changedLeg.Factor;
                        }
                    }
                }
                finally
                {
                    _isSyncing = false;
                }
            }

            PreviewNeedsRefresh?.Invoke(this, EventArgs.Empty);
        }

        public Dictionary<string, double> GetLegFactors()
        {
            var dict = new Dictionary<string, double>();
            foreach (var leg in Legs)
            {
                dict[leg.Label] = leg.Factor;
            }
            return dict;
        }

        public void ResetFactors()
        {
            foreach (var leg in Legs)
            {
                leg.Factor = 1.0;
            }
            PreviewNeedsRefresh?.Invoke(this, EventArgs.Empty);
        }

        public void ToggleFlipX() => IsFlippedX = !IsFlippedX;
        public void ToggleFlipY() => IsFlippedY = !IsFlippedY;

        public void CycleHorizontalLeg()
        {
            if (Shape2D.Legs.Count > 1)
            {
                HorizontalLegIndex = (HorizontalLegIndex + 1) % Shape2D.Legs.Count;
            }
        }

        public void ResetOrientation()
        {
            _isFlippedX = false;
            _isFlippedY = false;
            _horizontalLegIndex = Shape2D.DefaultHorizontalLegIndex;
            _dimOffset = 35.0;
            OnPropertyChanged(nameof(IsFlippedX));
            OnPropertyChanged(nameof(IsFlippedY));
            OnPropertyChanged(nameof(HorizontalLegIndex));
            OnPropertyChanged(nameof(DimOffset));
            OnPropertyChanged(nameof(DimOffsetDisplay));
            PreviewNeedsRefresh?.Invoke(this, EventArgs.Empty);
        }

        public void MarkAssigned(string pngPath)
        {
            ImagePath = pngPath;
            IsAssigned = true;
            StatusText = "Đã gán vào Rebar Shape";
            StatusIcon = "✅";
            StatusBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 135, 90));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }
}
