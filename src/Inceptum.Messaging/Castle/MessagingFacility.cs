﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Runtime.CompilerServices;
using Castle.Core;
using Castle.MicroKernel;
using Castle.MicroKernel.Context;
using Castle.MicroKernel.Facilities;
using Castle.MicroKernel.Registration;
using Inceptum.Core;
using Inceptum.Messaging.Configuration;
using Inceptum.Messaging.Contract;
using Inceptum.Messaging.Serialization;

namespace Inceptum.Messaging.Castle
{


    public class MessagingConfiguration : IMessagingConfiguration
    {
        public MessagingConfiguration()
        {
            Transports = new Dictionary<string, TransportInfo>();
            Endpoints = new Dictionary<string, Endpoint>();
        }

        public IDictionary<string, TransportInfo> Transports { get; set; }
        public IDictionary<string, Endpoint> Endpoints { get; set; }
        public IDictionary<string, TransportInfo> GetTransports()
        {
            return Transports;
        }

        public IDictionary<string, Endpoint> GetEndpoints()
        {
            return Endpoints;
        }
    }
    public class MessagingFacility : AbstractFacility
    {
        private IDictionary<string, TransportInfo> m_Transports;
        private IDictionary<string, JailStrategy> m_JailStrategies;
        private readonly List<IHandler> m_SerializerWaitList = new List<IHandler>();
        private readonly List<IHandler> m_SerializerFactoryWaitList = new List<IHandler>();
        private readonly List<IHandler> m_MessageHandlerWaitList = new List<IHandler>();
        private IMessagingEngine m_MessagingEngine;
        private readonly List<Action<IKernel>> m_InitSteps = new List<Action<IKernel>>();
        private List<ITransportFactory> m_TransportFactories=new List<ITransportFactory>();

        public IDictionary<string, TransportInfo> Transports
        {
            get { return m_Transports; }
            set { m_Transports = value; }
        }

        public IDictionary<string, JailStrategy> JailStrategies
        {
            get { return m_JailStrategies; }
            set { m_JailStrategies = value; }
        }

        private IMessagingConfiguration MessagingConfiguration { get; set; }

        public MessagingFacility()
        {
        }

        public MessagingFacility WithTransportFactory(ITransportFactory factory)
        {
            m_TransportFactories.Add(factory);
            return this;
        }
       
        
        public MessagingFacility WithConfiguration(IMessagingConfiguration configuration)
        {
            MessagingConfiguration=configuration;
            return this;
        }

        public MessagingFacility(IDictionary<string, TransportInfo> transports, IDictionary<string, JailStrategy> jailStrategies = null)
        {
            Transports = transports;
            m_JailStrategies = jailStrategies;
        }

        public void AddInitStep(Action<IKernel> step)
        {
            m_InitSteps.Add(step);
        }

        protected override void Init()
        {
          foreach (var initStep in m_InitSteps)
            {
                initStep(Kernel);
            }
            var messagingConfiguration = MessagingConfiguration;
            var transports = m_Transports;

            if (messagingConfiguration != null && transports != null)
                throw new Exception("Messaging facility can be configured via transports parameter or via MessagingConfiguration property, not both.");

  

            if (messagingConfiguration != null)
            {
                transports = messagingConfiguration.GetTransports();

                if (Kernel.HasComponent(typeof (IEndpointProvider)))
                {
                    throw new Exception("IEndpointProvider already registered in container, can not register IEndpointProvider from MessagingConfiguration");
                }
                Kernel.Register(Component.For<IEndpointProvider>().Forward<ISubDependencyResolver>().ImplementedBy<EndpointResolver>().Named("EndpointResolver").DependsOn(new { endpoints = messagingConfiguration.GetEndpoints() }));
                var endpointResolver = Kernel.Resolve<ISubDependencyResolver>("EndpointResolver");
                Kernel.Resolver.AddSubResolver(endpointResolver);
            }

            m_MessagingEngine = new MessagingEngine(new TransportResolver(transports ?? new Dictionary<string, TransportInfo>(), m_JailStrategies), m_TransportFactories.ToArray());

            Kernel.Register(
                 Component.For<IMessagingEngine>().Instance(m_MessagingEngine)
                );
            Kernel.ComponentRegistered += onComponentRegistered;
            Kernel.ComponentModelCreated += ProcessModel;
        }

        protected override void Dispose()
        {
            m_MessagingEngine.Dispose();
            base.Dispose();
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        private void onComponentRegistered(string key, IHandler handler)
        {
         

            if ((bool)(handler.ComponentModel.ExtendedProperties["IsSerializerFactory"] ?? false))
            {
                if (handler.CurrentState == HandlerState.WaitingDependency)
                {
                    m_SerializerFactoryWaitList.Add(handler);
                }
                else
                {
                    registerSerializerFactory(handler);
                }
            }

            var messageHandlerFor = handler.ComponentModel.ExtendedProperties["MessageHandlerFor"] as string[];
            if (messageHandlerFor!=null && messageHandlerFor.Length > 0)
            {
                if (handler.CurrentState == HandlerState.WaitingDependency)
                {
                    m_MessageHandlerWaitList.Add(handler);
                }
                else
                {
                    handler.Resolve(CreationContext.CreateEmpty());
                }
            }

            processWaitList();
        }

        private void registerSerializerFactory(IHandler handler)
        {
            m_MessagingEngine.SerializationManager.RegisterSerializerFactory(Kernel.Resolve(handler.ComponentModel.Name, typeof(ISerializerFactory)) as ISerializerFactory);
        }
 

        private void onHandlerStateChanged(object source, EventArgs args)
        {
            processWaitList();
        }




        private void processWaitList()
        {
            foreach (var handler in m_MessageHandlerWaitList.Where(handler => handler.CurrentState == HandlerState.Valid))
            {
                handler.Resolve(CreationContext.CreateEmpty());
            }

            foreach (var factoryHandler in m_SerializerFactoryWaitList.ToArray().Where(factoryHandler => factoryHandler.CurrentState == HandlerState.Valid))
            {
                registerSerializerFactory(factoryHandler);
                m_SerializerWaitList.Remove(factoryHandler);
            }
        }


        public void ProcessModel(ComponentModel model)
        {
            var messageHandlerFor = model.ExtendedProperties["MessageHandlerFor"] as string[];
            if (messageHandlerFor != null && messageHandlerFor.Length > 0)
            {
                model.CustomComponentActivator = typeof(MessageHandlerActivator);
            }

            if (model.Services.Contains(typeof(ISerializerFactory)))
            {
                model.ExtendedProperties["IsSerializerFactory"] = true;
            }
            else
            {
                model.ExtendedProperties["IsSerializerFactory"] = false;
            }
        }
    }
}