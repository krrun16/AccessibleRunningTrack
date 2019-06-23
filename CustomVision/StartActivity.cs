using System;
using Android;
using Android.App;
using Android.Content;
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
        private Button startButton, startButtonTutorial;
        private Button StartButton => startButton ?? 
            (startButton = FindViewById<Button>(Resource.Id.start_btn));
        private Button StartButtonTutorial => startButtonTutorial ?? 
            (startButtonTutorial = FindViewById<Button>(Resource.Id.start_btn_tutorial));

        private static readonly string[] permissions = {
            Manifest.Permission.WriteExternalStorage,
            Manifest.Permission.Camera
        };

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            ActivityCompat.RequestPermissions(this, permissions, PERMISSION_ALL);
            SetContentView(Resource.Layout.StartScreen);
           
            StartButton.Click += StartButton_Click;
            StartButtonTutorial.Click += StartButtonTutorial_Click;
        }

        private void StartButton_Click(object sender, EventArgs e)
        {
            Intent m_activity = new Intent(this, typeof(MainActivity));
            m_activity.PutExtra("wait", true);
            StartActivity(m_activity);
        }

        private void StartButtonTutorial_Click(object sender, EventArgs e)
        {
            Intent m_activity = new Intent(this, typeof(MainActivity));
            m_activity.PutExtra("wait", false);
            StartActivity(m_activity);
        }
    }
}