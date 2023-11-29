using Amazon.CDK;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ReceiptServiceCDK
{
    sealed class Program
    {
        public static void Main(string[] args)
        {
            var app = new App();
            new ReceiptServiceStack(app, "ReceiptServiceStack");
            app.Synth();
        }
    }
}
