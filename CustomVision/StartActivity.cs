using System;
using System.IO;
using Android;
using Android.App;
using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Media;
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

            /* Testing video footage at periodic time stamps
             * MediaMetadataRetriever mRetriever = new MediaMetadataRetriever();
            AssetManager assetManager = Application.Context.Assets;
            AssetFileDescriptor afd = assetManager.OpenFd("test2_rotated.mp4");
            mRetriever.SetDataSource(afd.FileDescriptor, afd.StartOffset, afd.Length);
            for (int i = 0; i < 100000000; i+= 1000000)
            {
                Bitmap test = mRetriever.GetFrameAtTime(i);
                int inputsize = imageTestClassifier.getInputSize();
                Bitmap scaledBitmap = Bitmap.CreateScaledBitmap(test, inputsize, inputsize, false);
                Bitmap resizedBitmap = scaledBitmap.Copy(Bitmap.Config.Argb8888, false);
                string result = imageTestClassifier.RecognizeImage2(resizedBitmap, "" + i);
                MemoryStream byteArrayOutputStream = new MemoryStream();
                resizedBitmap.Compress(Bitmap.CompressFormat.Png, 100, byteArrayOutputStream);
                Log("created png", DateTime.Now, ""+i);
                byte[] png = byteArrayOutputStream.ToArray();
                SaveTestBitmap(png, "" + i);
                Log(result, DateTime.Now, "" + i);
            }*/
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
                    Log("saved image", DateTime.Now, prefix);
                }
            }
        }

        public Bitmap getBitmapFromAssets(string fileName)
        {
            AssetManager assetManager = Application.Context.Assets;
            System.IO.Stream istr = assetManager.Open(fileName);
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