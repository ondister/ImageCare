namespace ImageCare.Core.Domain.Folders;

public interface IDriveModelsFactory
{
	public DriveModel? CreateDriveModel(DriveInfo driveInfo);
}