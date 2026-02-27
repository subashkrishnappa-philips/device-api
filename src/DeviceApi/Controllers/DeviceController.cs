using Microsoft.AspNetCore.Mvc;
using DeviceApi.Models;

namespace DeviceApi.Controllers
{
    /// <summary>
    /// Handles device management operations.
    /// </summary>
    [ApiController]
    [Route("api")]
    [Produces("application/json")]
    public class DeviceController : ControllerBase
    {
        private readonly ILogger<DeviceController> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceController"/> class.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        public DeviceController(ILogger<DeviceController> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Updates the information associated with a specific device.
        /// </summary>
        /// <remarks>
        /// Associates the given username with the device identified by the provided serial number.
        ///
        /// **Sample Request:**
        ///
        ///     POST /api/UpdateDeviceInformation
        ///     {
        ///         "SerialNumber": "SN-20240001-XYZ",
        ///         "Username": "john.doe",
        ///         "NewPartNumber": "PN-4400-REV-B"
        ///     }
        ///
        /// **Sample Response (200 OK):**
        ///
        ///     {
        ///         "success": true,
        ///         "message": "Device information updated successfully.",
        ///         "serialNumber": "SN-20240001-XYZ",
        ///         "username": "john.doe",
        ///         "updatedAt": "2026-02-27T10:30:00Z",
        ///         "newPartNumber": "PN-4400-REV-B"
        ///     }
        /// </remarks>
        /// <param name="request">The device information update payload.</param>
        /// <returns>A response indicating the outcome of the update operation.</returns>
        /// <response code="200">Device information updated successfully.</response>
        /// <response code="400">The request payload is invalid or missing required fields.</response>
        /// <response code="500">An unexpected server error occurred.</response>
        [HttpPost("UpdateDeviceInformation")]
        [ProducesResponseType(typeof(UpdateDeviceInformationResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public IActionResult UpdateDeviceInformation([FromBody] UpdateDeviceInformationRequest request)
        {
            if (!ModelState.IsValid)
            {
                _logger.LogWarning(
                    "Invalid model state for UpdateDeviceInformation request. SerialNumber: {SerialNumber}",
                    request?.SerialNumber);
                return BadRequest(ModelState);
            }

            _logger.LogInformation(
                "UpdateDeviceInformation called. SerialNumber: {SerialNumber}, Username: {Username}",
                request.SerialNumber,
                request.Username);

            // TODO: Replace with actual business logic / persistence layer.
            var response = new UpdateDeviceInformationResponse
            {
                Success       = true,
                Message       = "Device information updated successfully.",
                SerialNumber  = request.SerialNumber,
                Username      = request.Username,
                UpdatedAt     = DateTime.UtcNow,
                NewPartNumber = request.NewPartNumber
            };

            return Ok(response);
        }
    }
}
