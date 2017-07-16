using System;

using NTumbleBit.BouncyCastle.Security;

namespace NTumbleBit.BouncyCastle.Crypto.Parameters
{
    internal class ParametersWithRandom
		: ICipherParameters
    {
        private readonly ICipherParameters	parameters;
		private readonly SecureRandom		random;

		public ParametersWithRandom(
            ICipherParameters	parameters,
            SecureRandom		random)
        {
			this.parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
			this.random = random ?? throw new ArgumentNullException(nameof(random));
		}

		public ParametersWithRandom(
            ICipherParameters parameters)
			: this(parameters, new SecureRandom())
        {
		}

		[Obsolete("Use Random property instead")]
		public SecureRandom GetRandom() => Random;

		public SecureRandom Random => random;

		public ICipherParameters Parameters => parameters;
	}
}
