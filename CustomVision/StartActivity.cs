using System;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Support.V4.App;
using Android.Support.V7.App;
using Android.Widget;

namespace CustomVision
{

    [Activity(Label = "@string/app_name", MainLauncher = true, Icon = "@mipmap/icon", Theme = "@style/MyTheme")]
    internal class StartActivity : AppCompatActivity
    {
        public static int PERMISSION_ALL = 0;
        private Button startButton;
        private Button StartButton => startButton ?? (startButton = FindViewById<Button>(Resource.Id.start_btn));

        private static readonly string[] permissions = {
            Manifest.Permission.WriteExternalStorage,
            Manifest.Permission.Camera
        };

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            ActivityCompat.RequestPermissions(this, permissions, PERMISSION_ALL);

            SetContentView(Resource.Layout.start_btn);
           
            StartButton.Click += StartButton_Click;
             

               
            
        }

        private void StartButton_Click(object sender, EventArgs e) {
            
                Intent m_activity = new Intent(this, typeof(MainActivity));
                StartActivity(m_activity);
            
            
        }
    }
}