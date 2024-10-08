using Application.Services;

namespace Application.Features.FindAvailableTutors;

public record FindAvailableTutorsQuery(
    Guid QualificationId,
    DateTime RequestedDateTime,
    string TimeZoneId
) : IRequest<ErrorOr<FindAvailableTutorsResponse>>;

public class FindAvailableTutorsQueryValidator : AbstractValidator<FindAvailableTutorsQuery>
{
    public FindAvailableTutorsQueryValidator()
    {
        RuleFor(q => q.QualificationId).NotEmpty().WithMessage("Qualification ID is required.");
        RuleFor(q => q.RequestedDateTime).NotEmpty().WithMessage("Requested date and time is required.")
            .GreaterThan(DateTime.UtcNow).WithMessage("Requested date and time must be in the future.");
    }
}

public class FindAvailableTutorsHandler(
    ITutorRepository tutorRepository,
    IQualificationRepository qualificationRepository,
    IBookingRepository bookingRepository,
    TimeZoneService timeZoneService)
    : IRequestHandler<FindAvailableTutorsQuery, ErrorOr<FindAvailableTutorsResponse>>
{
    public async Task<ErrorOr<FindAvailableTutorsResponse>> Handle(
        FindAvailableTutorsQuery request,
        CancellationToken cancellationToken)
    {
        var qualification = await qualificationRepository.GetByIdAsync(request.QualificationId, cancellationToken);
        if (qualification == null)
        {
            return Error.NotFound("QualificationNotFound", $"A qualification with the ID '{request.QualificationId}' was not found.");
        }

        var tutors = await tutorRepository.GetTutorsWithQualificationAndAvailabilitiesAsync(request.QualificationId, cancellationToken);

        var availableTutors = new List<AvailableTutorDto>();
        var utcRequestedDateTime = timeZoneService.ConvertToUtc(request.RequestedDateTime, request.TimeZoneId);
        var utcEndDateTime = utcRequestedDateTime.AddHours(1);

        foreach (var tutor in tutors)
        {
            if (IsTutorAvailable(tutor, utcRequestedDateTime, utcEndDateTime))
            {
                var overlappingBooking = await bookingRepository.GetOverlappingBookingAsync(
                    tutor.Id,
                    utcRequestedDateTime,
                    utcEndDateTime,
                    cancellationToken);

                if (overlappingBooking == null)
                {
                    availableTutors.Add(new AvailableTutorDto(
                        tutor.Id,
                        tutor.Name,
                        timeZoneService.ConvertFromUtc(utcRequestedDateTime, tutor.TimeZoneId),
                        timeZoneService.ConvertFromUtc(utcEndDateTime, tutor.TimeZoneId)
                    ));
                }
            }
        }

        return new FindAvailableTutorsResponse(
            request.QualificationId,
            qualification.Name,
            utcRequestedDateTime,
            availableTutors
        );
    }

    private bool IsTutorAvailable(Tutor tutor, DateTime utcStartDateTime, DateTime utcEndDateTime)
    {
        var availability = tutor.Availabilities.FirstOrDefault(a => a.Day == utcStartDateTime.DayOfWeek);

        if (availability == null)
        {
            return false;
        }

        return availability.TimeSlots.Any(ts =>
            ts.ContainsRange(utcStartDateTime.TimeOfDay, utcEndDateTime.TimeOfDay));
    }
}

public record FindAvailableTutorsResponse(
    Guid QualificationId,
    string QualificationName,
    DateTime RequestedDateTime,
    List<AvailableTutorDto> AvailableTutors
);

public record AvailableTutorDto(
    Guid Id,
    string Name,
    DateTime StartTimeUtc,
    DateTime EndTimeUtc
);