﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace LanPlayServer
{
    class RateLimiterBucket
    {
        public int      Count;
        public DateTime StartTime;
    }

    static class RateLimiter
    {
        private static uint   _rateLimit;
        private static double _rateLimitTime;

        private static readonly Dictionary<IPAddress, RateLimiterBucket> _rateLimiterBucket = new Dictionary<IPAddress, RateLimiterBucket>();

        private static readonly object lockObj = new object();

        public static void SetRateLimit(uint rateLimit, double rateLimitTime)
        {
            _rateLimit     = rateLimit;
            _rateLimitTime = rateLimitTime;
        }

        public static bool IsRateLimited(IPAddress ipAddress)
        {
            lock (lockObj)
            {
                if (!_rateLimiterBucket.ContainsKey(ipAddress))
                {
                    _rateLimiterBucket[ipAddress] = new RateLimiterBucket()
                    {
                        Count = 1,
                        StartTime = DateTime.Now
                    };

                    return false;
                }
                else
                {
                    RateLimiterBucket rateLimiterBucket = _rateLimiterBucket[ipAddress];
                    TimeSpan elapsedTime = DateTime.Now - rateLimiterBucket.StartTime;

                    if (elapsedTime >= TimeSpan.FromMilliseconds(_rateLimitTime))
                    {
                        rateLimiterBucket.Count = 1;
                        rateLimiterBucket.StartTime = DateTime.Now;
                    }
                    else
                    {
                        if (rateLimiterBucket.Count >= _rateLimit)
                        {
                            return true;
                        }
                        else
                        {
                            rateLimiterBucket.Count++;
                        }
                    }
                }

                return false;
            }
        }
    }
}