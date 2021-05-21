using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Scrimp.Models
{
    public class Episode
    {
        public string Title { get; set; }
        public string Page { get; set; }
        public string Description { get; set; }
        public DateTime AirDate { get; set; }
        public int Number { get; set; }
        public int Season { get; set; }
        public string VideoSource { get; set; }
        public string FileName { get; set; }
    }
}
