﻿using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace Flow.Launcher.Converters
{
    public class OrdinalConverter : IValueConverter
    {
        public object Convert(object value, System.Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ListBoxItem listBoxItem
                && ItemsControl.ItemsControlFromItemContainer(listBoxItem) is ListBox listBox)
                return listBox.ItemContainerGenerator.IndexFromContainer(listBoxItem) + 1;

            return 0;
        }

        public object ConvertBack(object value, System.Type targetType, object parameter, CultureInfo culture) => throw new System.InvalidOperationException();
    }
}
