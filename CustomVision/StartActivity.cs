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

        // setting up optional checkboxes for tutorial. NOTE these will be removed before the study.
        // we need to make a decision as to what we will do for the final study.
        private CheckBox tiltPhotos, percent25, percent35, backCamera;
        private CheckBox TiltPhotos => tiltPhotos ?? (tiltPhotos = FindViewById<CheckBox>(Resource.Id.tilt_photos));
        private CheckBox Percent25 => percent25 ?? (percent25 = FindViewById<CheckBox>(Resource.Id.twenty_five_percent));
        private CheckBox Percent35 => percent35 ?? (percent35 = FindViewById<CheckBox>(Resource.Id.thirty_five_percent));
        private CheckBox BackCamera => backCamera ?? (backCamera = FindViewById<CheckBox>(Resource.Id.back_camera_use));

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
            m_activity.PutExtra("tiltPhotos", TiltPhotos.Checked);
            m_activity.PutExtra("25Percent", Percent25.Checked);
            m_activity.PutExtra("35Percent", Percent35.Checked);
            m_activity.PutExtra("backCamera", BackCamera.Checked);
            StartActivity(m_activity);
        }
    }
}