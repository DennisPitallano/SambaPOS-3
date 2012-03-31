﻿using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text.RegularExpressions;
using Samba.Domain.Models.Accounts;
using Samba.Domain.Models.Resources;
using Samba.Infrastructure;
using Samba.Infrastructure.Data;
using Samba.Localization.Properties;
using Samba.Persistance.Data;
using Samba.Services.Common;

namespace Samba.Services.Implementations.AccountModule
{
    [Export(typeof(IAccountService))]
    public class AccountService : AbstractService, IAccountService
    {
        [ImportingConstructor]
        public AccountService()
        {
            ValidatorRegistry.RegisterDeleteValidator(new AccountTemplateDeleteValidator());
            ValidatorRegistry.RegisterDeleteValidator(new AccountDeleteValidator());
            ValidatorRegistry.RegisterSaveValidator(new NonDuplicateSaveValidator<Resource>(string.Format(Resources.SaveErrorDuplicateItemName_f, Resources.Account)));
            ValidatorRegistry.RegisterSaveValidator(new NonDuplicateSaveValidator<ResourceTemplate>(string.Format(Resources.SaveErrorDuplicateItemName_f, Resources.AccountTemplate)));
            ValidatorRegistry.RegisterSaveValidator(new NonDuplicateSaveValidator<AccountTransactionTemplate>(string.Format(Resources.SaveErrorDuplicateItemName_f, Resources.AccountTransactionTemplate)));
            ValidatorRegistry.RegisterSaveValidator(new NonDuplicateSaveValidator<AccountTransactionDocumentTemplate>(string.Format(Resources.SaveErrorDuplicateItemName_f, Resources.DocumentTemplate)));
        }

        private int? _accountCount;
        public int GetAccountCount()
        {
            return (int)(_accountCount ?? (_accountCount = Dao.Count<Resource>()));
        }

        public void CreateNewTransactionDocument(Account selectedAccount, AccountTransactionDocumentTemplate documentTemplate, string description, decimal amount)
        {
            using (var w = WorkspaceFactory.Create())
            {
                var document = documentTemplate.CreateDocument(selectedAccount, description, amount);
                w.Add(document);
                w.CommitChanges();
            }
        }

        public decimal GetAccountBalance(int accountId)
        {
            return Dao.Sum<AccountTransactionValue>(x => x.Debit - x.Credit, x => x.AccountId == accountId);
        }

        public string GetCustomData(Resource resource, string fieldName)
        {
            var pattern = string.Format("\"Name\":\"{0}\",\"Value\":\"([^\"]+)\"", fieldName);
            return Regex.IsMatch(resource.CustomData, pattern)
                ? Regex.Match(resource.CustomData, pattern).Groups[1].Value : "";
        }

        public string GetCustomData(Account account, string fieldName)
        {
            return "";
        }

        public string GetDescription(AccountTransactionDocumentTemplate documentTemplate, Account account)
        {
            var result = documentTemplate.DescriptionTemplate;
            if (string.IsNullOrEmpty(result)) return result;
            while (Regex.IsMatch(result, "\\[:([^\\]]+)\\]"))
            {
                var match = Regex.Match(result, "\\[:([^\\]]+)\\]");
                result = result.Replace(match.Groups[0].Value, GetCustomData(account, match.Groups[1].Value));
            }
            if (result.Contains("[MONTH]")) result = result.Replace("[MONTH]", DateTime.Now.ToMonthName());
            if (result.Contains("[NEXT MONTH]")) result = result.Replace("[NEXT MONTH]", DateTime.Now.ToNextMonthName());
            if (result.Contains("[WEEK]")) result = result.Replace("[WEEK]", DateTime.Now.WeekOfYear().ToString());
            if (result.Contains("[NEXT WEEK]")) result = result.Replace("[NEXT WEEK]", (DateTime.Now.NextWeekOfYear()).ToString());
            if (result.Contains("[YEAR]")) result = result.Replace("[YEAR]", (DateTime.Now.Year).ToString());
            if (result.Contains("[ACCOUNT NAME]")) result = result.Replace("[ACCOUNT NAME]", account.Name);
            return result;
        }

        public decimal GetDefaultAmount(AccountTransactionDocumentTemplate documentTemplate, Account account)
        {
            decimal result = 0;
            if (!string.IsNullOrEmpty(documentTemplate.DefaultAmount))
            {
                var da = documentTemplate.DefaultAmount;
                if (Regex.IsMatch(da, "\\[:([^\\]]+)\\]"))
                {
                    var match = Regex.Match(da, "\\[:([^\\]]+)\\]");
                    da = GetCustomData(account, match.Groups[1].Value);
                    decimal.TryParse(da, out result);
                }
                else if (da == string.Format("[{0}]", Resources.Balance))
                    result = Math.Abs(GetAccountBalance(account.Id));
                else decimal.TryParse(da, out result);
            }
            return result;
        }

        public void UpdateAccountState(int accountId, int stateId)
        {
            using (var w = WorkspaceFactory.Create())
            {
                var csid = w.Last<ResourceStateValue>(x => x.ResoruceId == accountId);
                if (csid == null || csid.StateId != stateId)
                {
                    var v = new ResourceStateValue { ResoruceId = accountId, Date = DateTime.Now, StateId = stateId };
                    w.Add(v);
                    w.CommitChanges();
                }
            }
        }

        public override void Reset()
        {
            _accountCount = null;
        }
    }

    public class AccountTemplateDeleteValidator : SpecificationValidator<ResourceTemplate>
    {
        public override string GetErrorMessage(ResourceTemplate model)
        {
            if (Dao.Exists<Resource>(x => x.ResourceTemplateId == model.Id))
                return string.Format(Resources.DeleteErrorUsedBy_f, Resources.AccountTemplate, Resources.Account);
            if (Dao.Exists<AccountTransactionDocumentTemplate>(x => x.MasterAccountTemplateId == model.Id))
                return string.Format(Resources.DeleteErrorUsedBy_f, Resources.AccountTemplate, Resources.DocumentTemplate);
            return "";
        }
    }

    public class AccountTransactionTemplateDeleteValidator : SpecificationValidator<AccountTransactionTemplate>
    {
        public override string GetErrorMessage(AccountTransactionTemplate model)
        {
            if (Dao.Exists<AccountTransactionDocumentTemplate>(x => x.TransactionTemplates.Any(y => y.Id == model.Id)))
                return string.Format(Resources.DeleteErrorUsedBy_f, Resources.AccountTransactionTemplate, Resources.DocumentTemplate);
            return "";
        }
    }

    public class AccountDeleteValidator : SpecificationValidator<Resource>
    {
        public override string GetErrorMessage(Resource model)
        {
            if (Dao.Exists<AccountTransactionValue>(x => x.AccountId == model.Id))
                return string.Format(Resources.DeleteErrorUsedBy_f, Resources.Account, Resources.AccountTransaction);
            return "";
        }
    }
}
