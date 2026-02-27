namespace DeviceApi.Models
{
    /// <summary>
    /// Response returned after updating device information.
    /// </summary>
    public class UpdateDeviceInformationResponse
    {
        /// <summary>
        /// Indicates whether the operation was successful.
        /// </summary>
        /// <example>true</example>
        public bool Success { get; set; }

        /// <summary>
        /// A human-readable message describing the result of the operation.
        /// </summary>
        /// <example>Device information updated successfully.</example>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// The serial number of the device that was updated.
        /// </summary>
        /// <example>SN-20240001-XYZ</example>
        public string SerialNumber { get; set; } = string.Empty;

        /// <summary>
        /// The username now associated with the device.
        /// </summary>
        /// <example>john.doe</example>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// The UTC timestamp at which the update occurred.
        /// </summary>
        /// <example>2026-02-27T10:30:00Z</example>
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// The new part number that was applied to the device.
        /// Null when no part number change was requested.
        /// </summary>
        /// <example>PN-4400-REV-B</example>
        public string? NewPartNumber { get; set; }
    }
}
