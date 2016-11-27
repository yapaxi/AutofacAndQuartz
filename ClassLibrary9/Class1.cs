using Autofac;
using Autofac.Core;
using Quartz;
using Quartz.Impl;
using Quartz.Spi;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace ClassLibrary9
{
    public class Class1
    {
        private static readonly ContainerBuilder _builder;
        public static readonly IContainer Container;

        private static Func<IComponentContext, Func<P1, T>> CreateCtor<T, P1>()
        {
            return (context) => (p1) => context.Resolve<T>(new TypedParameter(typeof(P1), p1));
        }

        private static Func<IComponentContext, Func<P1, P2, T>> CreateCtor<T, P1, P2>()
        {
            return (context) => (p1, p2) => context.Resolve<T>
            (
                new TypedParameter(typeof(P1), p1),
                new TypedParameter(typeof(P2), p2)
            );
        }

        static Class1()
        {
            _builder = new ContainerBuilder();
            _builder.RegisterModule<QuartzModule>();

            _builder.RegisterType<SomeCreator>().As<ICreator1>();
            _builder.RegisterType<SomeCreator>().As<ICreator2>();

            _builder.Register(CreateCtor<Marketplace, ICreator1>());
            _builder.Register(CreateCtor<Marketplace, ICreator2>());

            Container = _builder.Build();
        }

        public static void Main(string[] args)
        {
            var scheduler = Container.Resolve<IScheduler>();
            scheduler.JobFactory = Container.Resolve<IJobFactory>();

            IDictionary<string, object> parameters = new Dictionary<string, object>();
            parameters[nameof(SomeJobConfiguration)] = new TypedParameter
            (
                typeof(SomeJobConfiguration),
                new SomeJobConfiguration() { Marketpace = Marketplace.Jet }
            );

            var job = JobBuilder
                .Create(typeof(SomeJob))
                .SetJobData(new JobDataMap(parameters))
                .Build();

            var trigger = TriggerBuilder.Create()
               .StartNow()
               .WithSimpleSchedule(schedule => schedule
                   .WithIntervalInSeconds(1)
                   .RepeatForever())
               .Build();

            scheduler.ScheduleJob(job, trigger);
            scheduler.Start();

            Process.GetCurrentProcess().WaitForExit();
        }
    }
    

    public class QuartzModule : Autofac.Module
    {
        /// <summary>
        ///     Override to add registrations to the container.
        /// </summary>
        /// <param name="builder">The builder through which components can be registered.</param>
        protected override void Load(ContainerBuilder builder)
        {
            builder.Register(c => new StdSchedulerFactory().GetScheduler())
                .As<IScheduler>()
                .InstancePerLifetimeScope();
            builder.Register(c => new QuartzJobFactory(Class1.Container))
                .As<IJobFactory>();

            builder.RegisterAssemblyTypes(GetJobAssembly()).Where(p => typeof(IJob).IsAssignableFrom(p));
        }

        protected virtual Assembly[] GetJobAssembly()
        {
            return new[] { Assembly.GetEntryAssembly() };
        }
    }

    public class QuartzJobFactory : IJobFactory
    {
        private readonly IContainer _container;

        public QuartzJobFactory(IContainer container)
        {
            _container = container;
        }

        public IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler)
        {
            var lifetimeScope = _container.BeginLifetimeScope();
            try
            {
                var parameters = bundle.JobDetail.JobDataMap.Select(e => e.Value).OfType<Parameter>().ToArray();
                var job = (ILifetimeJob)lifetimeScope.Resolve(bundle.JobDetail.JobType, parameters);
                job.Scope = lifetimeScope;
                return job;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                lifetimeScope.Dispose();
                throw;
            }
        }

        public void ReturnJob(IJob job)
        {
            ((ILifetimeJob)job).Scope.Dispose();
        }
    }

    public enum Marketplace
    {
        Invalid = 0,
        Amazon = 1,
        Jet = 2
    }
    
    public class SomeJobConfiguration
    {
        public Marketplace Marketpace { get; set; }
    }

    public interface ILifetimeJob : IJob
    {
        ILifetimeScope Scope { get; set; }
    }

    public class SomeJob : ILifetimeJob
    {
        ILifetimeScope ILifetimeJob.Scope { get; set; }

        private readonly ICreator1 _creator1;
        private readonly ICreator2 _creator2;
        private readonly SomeJobConfiguration _configuration;

        public SomeJob(
            SomeJobConfiguration configuration,
            Func<Marketplace, ICreator1> creater1Factory,
            Func<Marketplace, ICreator2> creator2Factory)
        {
            _configuration = configuration;
            _creator1 = creater1Factory(configuration.Marketpace);
            _creator2 = creator2Factory(configuration.Marketpace);
        }

        public void Execute(IJobExecutionContext context)
        {
            Console.WriteLine($"SomeJob.Execute for marketplace {_configuration.Marketpace}");
            Console.WriteLine(_creator1.Do(5));
            Console.WriteLine(_creator2.Do(5));
        }
    }

    public interface ICreator1 : IDisposable { int Do(int x); }
    public interface ICreator2 : IDisposable { string Do(int x); }

    public class SomeCreator : ICreator1, ICreator2
    {
        private readonly Marketplace _marketplace;

        public SomeCreator(Marketplace marketplaceId)
        {
            _marketplace = marketplaceId;
            Console.WriteLine($"{this.GetType().Name} .ctor: {_marketplace}");
        }

        int ICreator1.Do(int x)
        {
            return x * 2;
        }

        string ICreator2.Do(int x)
        {
            return x.ToString();
        }

        public void Dispose()
        {
            Console.WriteLine($"{this.GetType().Name} Dispose");
        }
    }
}
