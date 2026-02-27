using System.ComponentModel.DataAnnotations;

namespace DeviceApi.Models
{
    /// <summary>
    /// Request payload for updating device information.
    /// </summary>
    public class UpdateDeviceInformationRequest
    {
        /// <summary>
        /// The unique serial number of the device.
        /// </summary>
        /// <example>SN-20240001-XYZ</example>
        [Required(ErrorMessage = "SerialNumber is required.")]
        [StringLength(100, MinimumLength = 1, ErrorMessage = "SerialNumber must be between 1 and 100 characters.")]
        public string SerialNumber { get; set; } = string.Empty;

        /// <summary>
        /// The username to associate with the device.
        /// </summary>
        /// <example>john.doe</example>
        [Required(ErrorMessage = "Username is required.")]
        [StringLength(50, MinimumLength = 1, ErrorMessage = "Username must be between 1 and 50 characters.")]
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// The new part number to associate with the device.
        /// Optional. When provided, the device catalogue entry will be updated.
        /// </summary>
        /// <example>PN-4400-REV-B</example>
        [StringLength(100, ErrorMessage = "NewPartNumber must be 100 characters or fewer.")]
        public string? NewPartNumber { get; set; }
    }
}
