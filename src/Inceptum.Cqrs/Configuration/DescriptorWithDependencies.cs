﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace Inceptum.Cqrs.Configuration
{
    public abstract class DescriptorWithDependencies : IBoundedContextDescriptor
    {
        private readonly Type[] m_Dependedncies = new Type[0];
        private readonly Func<Func<Type, object>, IEnumerable<object>> m_ResolveDependedncies;

        protected DescriptorWithDependencies(params object[] dependencies)
        {
            m_ResolveDependedncies = r => dependencies;
        }

        protected DescriptorWithDependencies(params Type[] dependencies)
        {
            m_Dependedncies = dependencies;
            m_ResolveDependedncies = dependencies.Select;

        }

        public IEnumerable<Type> GetDependedncies()
        {
            return m_Dependedncies;
        }

        public void Create(BoundedContext boundedContext, Func<Type, object> resolve)
        {
           
            ResolvedDependencies = m_ResolveDependedncies(resolve);
            Create(boundedContext);
        }

        protected IEnumerable<object> ResolvedDependencies { get; private set; }

        protected virtual void Create(BoundedContext boundedContext)
        {
            
        }

        public virtual void Process(BoundedContext boundedContext, CqrsEngine cqrsEngine)
        {
            
        }


    }
}