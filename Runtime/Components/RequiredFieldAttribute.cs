using UnityEngine;

namespace MrPathV2.Runtime.Components
{
    public class RequiredFieldAttribute : PropertyAttribute
    {
        public RequiredFieldAttribute() { }

        public RequiredFieldAttribute(string errorMessage)
        {
            ErrorMessage = errorMessage;
        }

        public RequiredFieldAttribute(string errorMessage, bool forceCheckInEditMode)
        {
            ErrorMessage = errorMessage;
            ForceCheckInEditMode = forceCheckInEditMode;
        }

        public string ErrorMessage { get; set; }

        public bool ForceCheckInEditMode { get; set; }
    }
}
