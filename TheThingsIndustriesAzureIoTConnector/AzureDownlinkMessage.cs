//---------------------------------------------------------------------------------
// Copyright (c) March 2022, devMobile Software
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
//---------------------------------------------------------------------------------
namespace devMobile.TheThingsIndustries.TheThingsIndustriesAzureIoTConnector
{
	using System;
	using System.Collections.Generic;

	using devMobile.TheThingsIndustries.TheThingsIndustriesAzureIoTConnector.Models;

	public class AzureDownlinkMessage
	{
		public static bool PortTryGet(IDictionary<string, string> properties, out byte port)
		{
			port = 0;

			if (!properties.ContainsKey("Port"))
			{
				return false;
			}

			if (!byte.TryParse(properties["Port"], out port))
			{
				return false;
			}

			if ((port < Constants.PortNumberMinimum) || port > (Constants.PortNumberMaximum))
			{
				return false;
			}

			return true;
		}

		public static bool ConfirmedTryGet(IDictionary<string, string> properties, out bool confirmed)
		{
			confirmed = false;

			if (!properties.ContainsKey("Confirmed"))
			{
				return true;
			}

			if (!bool.TryParse(properties["Confirmed"], out confirmed))
			{
				return false;
			}

			return true;
		}

		public static bool PriorityTryGet(IDictionary<string, string> properties, out DownlinkPriority priority)
		{
			priority = DownlinkPriority.Normal;

			if (!properties.ContainsKey("Priority"))
			{
				return true;
			}

			if (!Enum.TryParse(properties["Priority"], true, out priority) || !Enum.IsDefined(typeof(DownlinkPriority), priority))
			{
				return false;
			}

			return true;
		}

		public static bool QueueTryGet(IDictionary<string, string> properties, out DownlinkQueue queue)
		{
			queue = DownlinkQueue.Push;

			if (!properties.ContainsKey("Queue"))
			{
				return true;
			}

			if (!Enum.TryParse(properties["Queue"], true, out queue) || !Enum.IsDefined(typeof(DownlinkQueue), queue))
			{
				return false;
			}

			return true;
		}
	}
}
