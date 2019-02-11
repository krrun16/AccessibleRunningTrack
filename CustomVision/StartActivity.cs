using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Support.V7.App;
using Android.Views;
using Android.Widget;

namespace CustomVision
{

    [Activity(Label = "@string/app_name", MainLauncher = true, Icon = "@mipmap/icon", Theme = "@style/MyTheme")]

    class StartActivity : AppCompatActivity
    {
        Button startButton;
        private Button StartButton => startButton ?? (startButton = FindViewById<Button>(Resource.Id.start_btn));


        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            SetContentView(Resource.Layout.start_btn);

            //SupportActionBar.SetDisplayHomeAsUpEnabled(false);
            //SupportActionBar.SetHomeButtonEnabled(false);

            StartButton.Click += StartButton_Click;
        }

        void StartButton_Click(object sender, EventArgs e) {
            var m_activity = new Intent(this, typeof(MainActivity_custom));
            this.StartActivity(m_activity);

        }

    }
}