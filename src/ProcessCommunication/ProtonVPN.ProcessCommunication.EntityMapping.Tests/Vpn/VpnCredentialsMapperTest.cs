﻿/*
 * Copyright (c) 2023 Proton AG
 *
 * This file is part of ProtonVPN.
 *
 * ProtonVPN is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * ProtonVPN is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with ProtonVPN.  If not, see <https://www.gnu.org/licenses/>.
 */

using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using ProtonVPN.Common.Vpn;
using ProtonVPN.Crypto;
using ProtonVPN.EntityMapping.Contracts;
using ProtonVPN.ProcessCommunication.Contracts.Entities.Crypto;
using ProtonVPN.ProcessCommunication.Contracts.Entities.Vpn;
using ProtonVPN.ProcessCommunication.EntityMapping.Vpn;

namespace ProtonVPN.ProcessCommunication.EntityMapping.Tests.Vpn
{
    [TestClass]
    public class VpnCredentialsMapperTest
    {
        private IEntityMapper _entityMapper;
        private VpnCredentialsMapper _mapper;

        private AsymmetricKeyPairIpcEntity _expectedAsymmetricKeyPairIpcEntity;
        private AsymmetricKeyPair _expectedAsymmetricKeyPair;

        [TestInitialize]
        public void Initialize()
        {
            _entityMapper = Substitute.For<IEntityMapper>();
            _mapper = new(_entityMapper);

            _expectedAsymmetricKeyPairIpcEntity = new AsymmetricKeyPairIpcEntity();
            _entityMapper.Map<AsymmetricKeyPair, AsymmetricKeyPairIpcEntity>(Arg.Any<AsymmetricKeyPair>())
                .Returns(_expectedAsymmetricKeyPairIpcEntity);

            _expectedAsymmetricKeyPair = new AsymmetricKeyPair(
                new SecretKey("PVPN", KeyAlgorithm.Unknown), new PublicKey("PVPN", KeyAlgorithm.Unknown));
            _entityMapper.Map<AsymmetricKeyPairIpcEntity, AsymmetricKeyPair>(Arg.Any<AsymmetricKeyPairIpcEntity>())
                .Returns(_expectedAsymmetricKeyPair);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _entityMapper = null;
            _mapper = null;

            _expectedAsymmetricKeyPairIpcEntity = null;
            _expectedAsymmetricKeyPair = null;
        }

        [TestMethod]
        public void TestMapLeftToRight_WithCertificate()
        {
            VpnCredentials entityToTest = new("CERT", new AsymmetricKeyPair(
                new SecretKey("PVPN", KeyAlgorithm.Ed25519), new PublicKey("PVPN", KeyAlgorithm.Ed25519)));

            VpnCredentialsIpcEntity result = _mapper.Map(entityToTest);

            Assert.IsNotNull(result);
            Assert.AreEqual(entityToTest.ClientCertPem, result.ClientCertPem);
            Assert.AreEqual(_expectedAsymmetricKeyPairIpcEntity, result.ClientKeyPair);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void TestMapRightToLeft_ThrowsWhenNull()
        {
            VpnCredentialsIpcEntity entityToTest = null;

            _mapper.Map(entityToTest);
        }

        [TestMethod]
        public void TestMapRightToLeft_WithCertificate()
        {
            VpnCredentialsIpcEntity entityToTest = new()
            {
                ClientCertPem = DateTime.UtcNow.Ticks.ToString(),
                ClientKeyPair = new AsymmetricKeyPairIpcEntity()
            };

            VpnCredentials result = _mapper.Map(entityToTest);

            Assert.IsNotNull(result);
            Assert.AreEqual(entityToTest.ClientCertPem, result.ClientCertPem);
            Assert.AreEqual(_expectedAsymmetricKeyPair, result.ClientKeyPair);
        }
    }
}