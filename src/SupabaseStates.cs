﻿using Supabase.Gotrue;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Threading;

namespace TmCGPTD
{
    public class SupabaseStates : ObservableObject
    {
        private static SupabaseStates? _instance;
        public static SupabaseStates Instance
        {
            get
            {
                _instance ??= new SupabaseStates();

                return _instance;
            }
        }

        public AesSettings? AesSettings { get; set; }
        public Supabase.Client? Supabase { get; set; }
        public ProviderAuthState? AuthState { get; set; }

        public readonly SemaphoreSlim SemaphoreSlim = new SemaphoreSlim(1, 1);
    }
}
