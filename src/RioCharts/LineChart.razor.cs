using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace RioCharts
{
    public partial class LineChart<TItem>
    {
        [Parameter] public string Width { get; set; } = "90%";
        [Parameter] public string Height { get; set; } = "90%";
        [Parameter] public IEnumerable<TItem>? Items { get; set; }
        [Parameter] public Expression<Func<TItem, object>>? XValue { get; set; }
        [Parameter] public Expression<Func<TItem, decimal>>? YValue { get; set; }

        private int startPadding = 50;
        private int endPadding = 25;
        private int topPadding = 50;
        private int bottomPadding = 25;
        private int boxWidth = 650;
        private int boxHeight = 350;

        private int origoX => startPadding;
        private int origoY => boxHeight - bottomPadding;

        private List<DataPoint<TItem>> dataPoints = new List<DataPoint<TItem>>();
      
        
        protected override void OnParametersSet()
        {
            PopulateDataPoints();
        }


        private void PopulateDataPoints()
        {
            if (Items == null) { return; }
            if (XValue == null || YValue == null) { return; }

            dataPoints = Items.Select(e => new DataPoint<TItem>
            {
                X = XValue.Compile().Invoke(e),
                Y = YValue.Compile().Invoke(e),
                Items = new List<TItem> { e }
            }).ToList();

            var kalle = "ff";
        }
    }
    public class DataPoint<TItem>
    {
        public object X { get; set; } = "*NA*";
        public decimal Y { get; set; }
        public IEnumerable<TItem>? Items { get; set; }
    }

}