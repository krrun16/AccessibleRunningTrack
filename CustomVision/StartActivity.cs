﻿using System;
using System.IO;
using Android;
using Android.App;
using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.OS;
using Android.Support.V4.App;
using Android.Support.V7.App;
using Android.Widget;

namespace CustomVision
{

    [Activity(Label = "@string/app_name", MainLauncher = true, Icon = "@mipmap/icon", Theme = "@style/MyTheme")]
    internal class StartActivity : AppCompatActivity
    {
        private static readonly object locker = new object();
        private static readonly ImageClassifier imageTestClassifier = new ImageClassifier();
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

            AssetManager assetManager = Application.Context.Assets;
            string[] assetsList = assetManager.List("");
            foreach (string fileName in assetsList)
            {
                if (fileName.Contains(".png"))
                {
                    Bitmap test = getBitmapFromAssets(fileName);
                    string result = imageTestClassifier.RecognizeImage2(test, fileName);
                    Log(result, DateTime.Now, fileName);
                }
            }
        }

        public Bitmap getBitmapFromAssets(string fileName)
        {
            AssetManager assetManager = Application.Context.Assets;
            Stream istr = assetManager.Open(fileName);
            Bitmap bitmap = BitmapFactory.DecodeStream(istr);
            istr.Close();
            return bitmap;
        }

        public static void Log(string label, DateTime currentTime, string prefix)
        {
            string msg = prefix + ".  " + currentTime.TimeOfDay + "_" + label;
            string sdCardPath = Android.OS.Environment.ExternalStorageDirectory.Path + "/CustomVision";
            string filePath = System.IO.Path.Combine(sdCardPath, "imagetest.txt");
            lock (locker)
            {
                using (StreamWriter write = new StreamWriter(filePath, true))
                {
                    write.Write(msg + "\n");
                }
            }
        }

        private void StartButton_Click(object sender, EventArgs e) {
            
                Intent m_activity = new Intent(this, typeof(MainActivity));
                StartActivity(m_activity);
            
            
        }
    }
}