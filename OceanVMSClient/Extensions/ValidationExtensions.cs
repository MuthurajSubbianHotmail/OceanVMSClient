using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Reflection;

namespace OceanVMSClient.Extensions
{
    public static class ValidationExtensions
    {
        // Use: ValidationExtensions.HasRequiredAttribute(() => model.Property)
        public static bool HasRequiredAttribute(Expression<Func<object>> accessor)
        {
            if (accessor == null)
                return false;

            MemberExpression? member = accessor.Body as MemberExpression;

            // handle value type conversions (e.g. boxing)
            if (member == null && accessor.Body is UnaryExpression unary && unary.Operand is MemberExpression m)
                member = m;

            if (member == null)
                return false;

            if (member.Member is PropertyInfo prop)
            {
                return Attribute.IsDefined(prop, typeof(RequiredAttribute));
            }

            if (member.Member is FieldInfo field)
            {
                return Attribute.IsDefined(field, typeof(RequiredAttribute));
            }

            return false;
        }
    }
}
