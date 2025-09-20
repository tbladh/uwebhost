<Query Kind="Program">
  <Namespace>System.Drawing</Namespace>
  <Namespace>System.Drawing.Imaging</Namespace>
  <Namespace>System.IO.Compression</Namespace>
  <Namespace>System.Windows.Forms</Namespace>
</Query>

void Main()
{
	// Open file dialog to select an image
	var openFileDialog = new OpenFileDialog
	{
		Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
		Title = "Select an Image to Convert to ICO and Web Icons"
	};

	if (openFileDialog.ShowDialog() != DialogResult.OK)
		return;

	string inputFile = openFileDialog.FileName;

	// Save file dialog to choose where to save the .zip file
	var saveFileDialog = new SaveFileDialog
	{
		Filter = "Zip Archive|*.zip",
		Title = "Save Icon Set Archive",
		FileName = Path.GetFileNameWithoutExtension(inputFile) + "_icons.zip"
	};

	if (saveFileDialog.ShowDialog() != DialogResult.OK)
		return;

	string zipFilePath = saveFileDialog.FileName;

	CreateIconSetArchive(inputFile, zipFilePath);
	Console.WriteLine("Icon set archive created successfully at: " + zipFilePath);
}

void CreateIconSetArchive(string imagePath, string zipFilePath)
{
	int[] iconSizes = { 16, 24, 32, 48, 64, 128, 256 }; // List of resolutions
	string tempDir = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(zipFilePath));
	string iconDir = Path.Combine(tempDir, "icons");
	string webIconsDir = Path.Combine(tempDir, "web-icons");
	Directory.CreateDirectory(iconDir);
	Directory.CreateDirectory(webIconsDir);

	string iconFilePath = Path.Combine(iconDir, "icon.ico");
	CreateIcoFile(imagePath, iconFilePath, iconSizes);

	using var originalImage = new Bitmap(imagePath);
	foreach (int size in iconSizes)
	{
		using var resizedImage = new Bitmap(originalImage, new Size(size, size));
		string webIconPath = Path.Combine(webIconsDir, $"favicon-{size}x{size}.png");
		resizedImage.Save(webIconPath, ImageFormat.Png);
	}

	// Create ZIP archive
	if (File.Exists(zipFilePath))
		File.Delete(zipFilePath);

	ZipFile.CreateFromDirectory(tempDir, zipFilePath);
	Directory.Delete(tempDir, true);
}

void CreateIcoFile(string imagePath, string iconPath, int[] iconSizes)
{
	using var originalImage = new Bitmap(imagePath);
	using var iconStream = new FileStream(iconPath, FileMode.Create);
	using var writer = new BinaryWriter(iconStream);

	writer.Write((short)0); // Reserved
	writer.Write((short)1); // ICO format
	writer.Write((short)iconSizes.Length); // Number of images

	long dataOffset = 6 + (16 * iconSizes.Length);
	var imageDataList = new List<byte[]>();

	foreach (int size in iconSizes)
	{
		using var resizedImage = new Bitmap(originalImage, new Size(size, size));
		using var memoryStream = new MemoryStream();
		resizedImage.Save(memoryStream, ImageFormat.Png);
		var imageData = memoryStream.ToArray();
		imageDataList.Add(imageData);

		writer.Write((byte)size); // Width
		writer.Write((byte)size); // Height
		writer.Write((byte)0);    // Colors (0 means no palette)
		writer.Write((byte)0);    // Reserved
		writer.Write((short)1);   // Color planes
		writer.Write((short)32);  // Bits per pixel
		writer.Write(imageData.Length); // Image data size
		writer.Write((int)dataOffset);  // Offset in file

		dataOffset += imageData.Length;
	}

	foreach (var imageData in imageDataList)
	{
		writer.Write(imageData);
	}
}
