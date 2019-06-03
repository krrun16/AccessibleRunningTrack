using System;
using System.IO;
using Android;
using Android.App;
using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.OS;
using Android.Support.V4.App;
using Android.Support.V7.App;
using Android.Util;
using Android.Widget;
using Org.Opencv.Android;

namespace CustomVision
{

    [Activity(Label = "@string/app_name", MainLauncher = true, Icon = "@mipmap/icon", Theme = "@style/MyTheme")]
    internal class StartActivity : AppCompatActivity
    {
        private static readonly object locker = new object();
      
        public static int PERMISSION_ALL = 0;
        private Button startButton, startButtonNoVideo;
        private Button StartButton => startButton ?? 
            (startButton = FindViewById<Button>(Resource.Id.start_btn));
        private Button StartButtonNoVideo => startButtonNoVideo ?? 
            (startButtonNoVideo = FindViewById<Button>(Resource.Id.start_btn_no_vid));

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
            StartButtonNoVideo.Click += StartButtonNoVideo_Click;

            AssetManager assetManager = Application.Context.Assets;
            string[] assetsList = assetManager.List("");
          

            foreach (string fileName in assetsList)
            {
                if (fileName.Contains(".png"))
                {
                    Bitmap test = getBitmapFromAssets(fileName);
                    //MainActivity.ImplementImageProcessing(test, prefix, fileName);

                }

            }
        }




        public static void SaveTestBitmap(byte[] data, string prefix)
        {
            lock (locker)
            {
                DateTime currentDate = DateTime.Now;
                long ts = currentDate.Ticks;
                string sdcardPath = Android.OS.Environment.ExternalStorageDirectory.Path + "/CustomVision";
                string fileName = prefix + ".png";
                string FilePath = System.IO.Path.Combine(sdcardPath, fileName);

                if (!File.Exists(FilePath))
                {
                    File.WriteAllBytes(FilePath, data);
                    //Log("saved image", DateTime.Now, prefix);
                }
            }
        }


        private Bitmap getBitmapFromAssets(string fileName)
        {
            AssetManager assetManager = Application.Context.Assets;
            System.IO.Stream istr = assetManager.Open(fileName);
            Bitmap bitmap = BitmapFactory.DecodeStream(istr);
            istr.Close();
            return bitmap;

        }

        private void StartButton_Click(object sender, EventArgs e)
        {
            Intent m_activity = new Intent(this, typeof(MainActivity));
            m_activity.PutExtra("show_video", true);
            StartActivity(m_activity);
        }

        private void StartButtonNoVideo_Click(object sender, EventArgs e)
        {
            Intent m_activity = new Intent(this, typeof(MainActivity));
            m_activity.PutExtra("show_video", false);
            StartActivity(m_activity);
        }
    }

     
        
    }