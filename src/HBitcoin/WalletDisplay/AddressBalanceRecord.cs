using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using NBitcoin;

namespace HBitcoin.WalletDisplay
{
    public class AddressBalanceRecord : INotifyPropertyChanged
	{
		// http://stackoverflow.com/questions/35582162/how-to-implement-inotifypropertychanged-in-c-sharp-6-0
		public event PropertyChangedEventHandler PropertyChanged;
		protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
		protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
		{
			if (EqualityComparer<T>.Default.Equals(field, value))
				return false;
			field = value;
			OnPropertyChanged(propertyName);
			return true;
		}

		private Script _scriptPubKey;
		public Script ScriptPubKey
		{
			get { return _scriptPubKey; }
			set { SetField(ref _scriptPubKey, value); }
		}
		private Money _confirmed;
		public Money Confirmed
		{
			get { return _confirmed; }
			set { SetField(ref _confirmed, value); }
		}
		private Money _unconfirmed;
		public Money Unconfirmed
		{
			get { return _unconfirmed; }
			set { SetField(ref _unconfirmed, value); }
		}
	}
}
