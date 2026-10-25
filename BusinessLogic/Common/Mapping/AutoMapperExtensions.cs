using AutoMapper;
using System.Collections;

namespace BusinessLogic.Common.Mapping;

public static class AutoMapperExtensions
{
    public static IMappingExpression<TSource, TDestination> ConfigureDbDestination<TSource, TDestination>(
        this IMappingExpression<TSource, TDestination> expression)
    {
        var destinationType = typeof(TDestination);

        var auditProperties = new[] { "CreatedOn", "CreatedById", "ModifiedOn", "ModifiedById" };
        foreach (var prop in auditProperties)
        {
            if (destinationType.GetProperty(prop) != null)
                expression.ForMember(prop, opt => opt.Ignore());
        }

        var properties = destinationType.GetProperties();
        foreach (var prop in properties)
        {
            if (typeof(IEnumerable).IsAssignableFrom(prop.PropertyType) && prop.PropertyType != typeof(string))
            {
                expression.ForMember(prop.Name, opt => opt.Ignore());
                continue;
            }

            if (prop.PropertyType.IsClass && prop.PropertyType != typeof(string))
            {
                expression.ForMember(prop.Name, opt => opt.Ignore());
            }
        }

        return expression;
    }
}
