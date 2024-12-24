using AutoMapper;

using ImageCare.Modules.Logging.Models;
using ImageCare.Modules.Logging.ViewModels;

namespace ImageCare.Modules.Logging.Mapping;

public sealed class LoggerMapper : Profile
{
    public LoggerMapper()
    {
        CreateMap<LogMessage, ErrorLogMessageViewModel>();
        CreateMap<LogMessage, WarningLogMessageViewModel>();
    }
}