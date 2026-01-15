using System;
using Shared.DTO.POModule;

namespace OceanVMSClient.Pages.InviceModule
{
    public static class InvoiceReviewHelpers
    {
        public static (Guid? PreviousReviewerId, string? PreviousReviewerName, decimal? PrevApprovedAmount, decimal? PrevWithheldAmount)
            GetPreviousReviewInfo(InvoiceDto? invoice, string currentTab)
        {
            if (invoice == null)
                return (null, null, null, null);

            var tab = (currentTab ?? string.Empty).Trim().ToLowerInvariant();

            return tab switch
            {
                // Initiator has no previous review
                "initiator" => (null, null, null, null),

                // Checker should see initiator's review — only if initiator review was required
                "checker" => invoice.IsInitiatorReviewRequired == true
                    ? (invoice.InitiatorReviewerID, invoice.InitiatorReviewerName, invoice.InitiatorApprovedAmount, invoice.InitiatorWithheldAmount)
                    : (null, null, null, null),

                // Validator: prefer checker's review when checker review is required; otherwise fall back to initiator's review.
                // If neither required/present, return nulls.
                "validator" => invoice.IsCheckerReviewRequired == true
                    ? (invoice.CheckerID, invoice.CheckerName, invoice.CheckerApprovedAmount, invoice.CheckerWithheldAmount)
                    : (invoice.IsInitiatorReviewRequired == true
                        ? (invoice.InitiatorReviewerID, invoice.InitiatorReviewerName, invoice.InitiatorApprovedAmount, invoice.InitiatorWithheldAmount)
                        : (null, null, null, null)),

                // Approver: prefer validator's review when required; otherwise fall back to checker, then initiator.
                // If none are required, return nulls.
                "approver" or "approver tab" => invoice.IsValidatorReviewRequired == true
                    ? (invoice.ValidatorID, invoice.ValidatorName, invoice.ValidatorApprovedAmount, invoice.ValidatorWithheldAmount)
                    : (invoice.IsCheckerReviewRequired == true
                        ? (invoice.CheckerID, invoice.CheckerName, invoice.CheckerApprovedAmount, invoice.CheckerWithheldAmount)
                        : (invoice.IsInitiatorReviewRequired == true
                            ? (invoice.InitiatorReviewerID, invoice.InitiatorReviewerName, invoice.InitiatorApprovedAmount, invoice.InitiatorWithheldAmount)
                            : (null, null, null, null))),
                "ap" or "ap approver" =>  invoice.IsApproverReviewRequired == true
                    ? (invoice.ApproverID, invoice.ApproverName, invoice.ApproverApprovedAmount, invoice.ApproverWithheldAmount)
                    : (invoice.IsValidatorReviewRequired == true
                        ? (invoice.ValidatorID, invoice.ValidatorName, invoice.ValidatorApprovedAmount, invoice.ValidatorWithheldAmount)
                        : (invoice.IsCheckerReviewRequired == true
                            ? (invoice.CheckerID, invoice.CheckerName, invoice.CheckerApprovedAmount, invoice.CheckerWithheldAmount)
                            : (invoice.IsInitiatorReviewRequired == true
                                ? (invoice.InitiatorReviewerID, invoice.InitiatorReviewerName, invoice.InitiatorApprovedAmount, invoice.InitiatorWithheldAmount)
                                : (null, null, null, null)))),
                _ => (null, null, null, null)
                //// AP / final approver sees approver's review — only if approver + all previous steps were required
                //"ap" or "ap approver" => (invoice.IsValidatorReviewRequired == true
                //                          && invoice.IsCheckerReviewRequired == true
                //                          && invoice.IsInitiatorReviewRequired == true
                //                          && invoice.IsApproverReviewRequired == true)
                //    ? (invoice.ApproverID ?? invoice.APReviewerId, invoice.ApproverName, invoice.ApproverApprovedAmount, invoice.ApproverWithheldAmount)
                //    : (null, null, null, null),




            };
        }
    }
}
