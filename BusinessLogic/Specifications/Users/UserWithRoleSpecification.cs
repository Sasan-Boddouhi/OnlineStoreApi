using Application.Common.Specifications;
using Application.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Specifications.Users
{
    public class UserWithRoleSpecification : BaseSpecification<User>
    {
        public UserWithRoleSpecification(int userId)
        {
            Criteria = u => u.UserId == userId;
            AddInclude(u => u.Employee);
            AddInclude(u => u.Employee.EmployeeType); // نیاز به زنجیره‌ای از Includes
        }

        // اگر AddInclude زنجیره‌ای پشتیبانی نمی‌کند، می‌توانید از ThenInclude استفاده کنید،
        // اما در اینجا فرض می‌کنیم که AddInclude قابلیت Expression<Func<TEntity, object>> را دارد و برای خصوصیات navigation کار می‌کند.
        // اگر Employee از نوع Employee است و EmployeeType یک خصوصیت در Employee، می‌توانید با یک include هر دو را بیاورید:
        // AddInclude(u => u.Employee); و سپس در LINQ بعداً از ThenInclude استفاده می‌کنید، اما در Specification معمولاً فقط Include سطح اول داریم.
        // برای Include دو سطحی، می‌توان از متد الحاقی استفاده کرد یا اینکه در Evaluator آن را پشتیبانی کرد.
        // به هر حال، این بستگی به پیاده‌سازی IGenericRepository دارد. در صورت نیاز می‌توانید از Include رشته‌ای استفاده کنید.
        // در اینجا فرض می‌کنیم که AddInclude از عبارت های پیچیده پشتیبانی می‌کند.
    }
}
