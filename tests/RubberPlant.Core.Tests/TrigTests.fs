module RubberPlant.Core.Tests.TrigTests

open System
open System.Security.Cryptography
open Expecto
open RubberPlant

let private toBits (x: float) = BitConverter.DoubleToInt64Bits x
let private ofBits (b: int64) = BitConverter.Int64BitsToDouble b

/// The error of `got`, in ulps of the exact value hi + lo (a double-double, so
/// the reference isn't rounded). The ulp is that of the exact value's binade.
let private ulpError (got: float) (hi: int64, lo: int64) =
    let hi, lo = ofBits hi, ofBits lo

    if hi = 0.0 then
        if got = 0.0 then 0.0 else infinity
    else
        let a = abs hi
        // At a power of two, an exact value just below it has the smaller ulp.
        let below = lo * float (sign hi) < 0.0
        let ulp = if below then a - Math.BitDecrement a else Math.BitIncrement a - a
        abs ((got - hi) - lo) / ulp

/// (angle, (sin hi, lo), (cos hi, lo)) as bits: the exact sine and cosine as
/// double-doubles, computed with mpmath at 2000 bits (sinpi and cospi of
/// (angle mod 360) / 180, so quarter turns are exact). Includes tiny and
/// subnormal angles around the 2^-900 threshold.
let private reference =
    [
        0x3fe0000000000000L, (0x3f81df37c4954c21L, 0x3c1543c7c2cf5d8fL), (0x3fefffb02599c9cdL, 0xbc78260f72c1b821L) // 0.5
        0x3ff0000000000000L, (0x3f91df0b2b89dd1eL, 0x3c35834d68148788L), (0x3feffec097f5af8aL, 0xbc818945ff801a15L) // 1.0
        0x4024000000000000L, (0x3fc63a1a7e0b738aL, 0xbc6744603e3937c7L), (0x3fef838b8c811c17L, 0x3c8682ec6bde69d5L) // 10.0
        0x4032000000000000L, (0x3fd3c6ef372fe950L, 0xbc7f506319fcfd19L), (0x3fee6f0e134454ffL, 0x3c8798ddb868c354L) // 18.0
        0x4032f33333333333L, (0x3fd4c8967c7e5d8fL, 0xbc7275b92d61f02cL), (0x3fee44033e0e1fbaL, 0xbc8aca04ff1c58e2L) // 18.95
        0x4036800000000000L, (0x3fd87de2a6aea963L, 0xbc672cedd3d5a610L), (0x3fed906bcf328d46L, 0x3c7457e610231ac2L) // 22.5
        0x4039b33333333333L, (0x3fdbc112087aacdcL, 0xbc7c1d1a12e060abL), (0x3fecd59f7a382ce0L, 0x3c851eef2acdcceeL) // 25.7
        0x403c000000000000L, (0x3fde0bd274245078L, 0x3c7d97f1131c42afL), (0x3fec411b4f6d2708L, 0xbc8abc92c5ff4313L) // 28.0
        0x403e000000000000L, (0x3fe0000000000000L, 0x0000000000000000L), (0x3febb67ae8584caaL, 0x3c8cec95d0b5c1e3L) // 30.0
        0x4041800000000000L, (0x3fe25abcf87c4978L, 0x3c8b805821236b88L), (0x3fea367e59158747L, 0xbc6476f2057c7a75L) // 35.0
        0x4042000000000000L, (0x3fe2cf2304755a5eL, 0xbc624bd9a522ca0dL), (0x3fe9e3779b97f4a8L, 0xbc7f506319fcfd19L) // 36.0
        0x40467ffffffdda3fL, (0x3fe6a09e667d8994L, 0x3c4d1d8dac575890L), (0x3fe6a09e6680ee05L, 0x3c69cedb6aa0d6dcL) // 44.999999999
        0x4046800000000000L, (0x3fe6a09e667f3bcdL, 0xbc8bdd3413b26456L), (0x3fe6a09e667f3bcdL, 0xbc8bdd3413b26456L) // 45.0
        0x40468000000225c1L, (0x3fe6a09e6680ee05L, 0x3c69cedb6aa0d6dcL), (0x3fe6a09e667d8994L, 0x3c4d1d8dac575890L) // 45.000000001
        0x404e000000000000L, (0x3febb67ae8584caaL, 0x3c8cec95d0b5c1e3L), (0x3fe0000000000000L, 0x8000000000000000L) // 60.0
        0x4052000000000000L, (0x3fee6f0e134454ffL, 0x3c8798ddb868c354L), (0x3fd3c6ef372fe950L, 0xbc7f506319fcfd19L) // 72.0
        0x4055400000000000L, (0x3fefe0d3b41815a2L, 0xbc6dc0ff3c26b1beL), (0x3fb64fd6b8c28103L, 0xbc5c8b5c051cd2dcL) // 85.0
        0x40567ffffff54339L, (0x3ff0000000000000L, 0xbbd1fb3e15b49594L), (0x3de7fcd3d8cadfbdL, 0x3a6f9b586e418cf4L) // 89.99999999
        0x4057af5c28f5c28fL, (0x3fefe3fb9d4f7bbfL, 0xbc83fe380218079bL), (0xbfb527850b07ee32L, 0x3c2201dc27c9f2bcL) // 94.74
        0x405e000000000000L, (0x3febb67ae8584caaL, 0x3c8cec95d0b5c1e3L), (0xbfe0000000000000L, 0x8000000000000000L) // 120.0
        0x40609428f5c28f5cL, (0x3fe78b340321e871L, 0xbc4e764bf5afe5f9L), (0xbfe5ac1fe1de3732L, 0x3c3661f5db2a31cbL) // 132.63
        0x4061300000000000L, (0x3fe59e6f5ae6a0a7L, 0x3c35fb5fa15d760dL), (0xbfe797c6a435ce85L, 0x3c88bb67ca8cc890L) // 137.5
        0x4062c00000000000L, (0x3fe0000000000000L, 0x0000000000000000L), (0xbfebb67ae8584caaL, 0xbc8cec95d0b5c1e3L) // 150.0
        0x40667fffffca501bL, (0x3e1dfc09ecf201d2L, 0xbabbf01077537b0cL), (0xbff0000000000000L, 0x3c3c189319cd9b0bL) // 179.9999999
        0x406680000035afe5L, (0xbe1dfc09ecf201d2L, 0x3abbf01077537b0cL), (0xbff0000000000000L, 0x3c3c189319cd9b0bL) // 180.0000001
        0x4069000000000000L, (0xbfd5e3a8748a0bf5L, 0xbc77371a64afcbd6L), (0xbfee11f642522d1cL, 0x3c894741676559d4L) // 200.0
        0x406bd00000000000L, (0xbfe59e6f5ae6a0a7L, 0xbc35fb5fa15d760dL), (0xbfe797c6a435ce85L, 0x3c88bb67ca8cc890L) // 222.5
        0x4070dffff583a53cL, (0xbfefffffffffff77L, 0x3c77ffa4d4012e50L), (0xbe876ce7d7742995L, 0xbb1382f4abd3c0ffL) // 269.99999
        0x4072c00000000000L, (0xbfebb67ae8584caaL, 0xbc8cec95d0b5c1e3L), (0x3fe0000000000000L, 0x8000000000000000L) // 300.0
        0x40767fffffffffffL, (0xbcd1df46a2529d39L, 0xb955c1d8becdd255L), (0x3ff0000000000000L, 0xb9a3f6a1db141fbaL) // 359.99999999999994
        0x3ddb7cdfd9d7bdbbL, (0x3d7eb443a0930a1fL, 0x3a0bfe6594c7ec4eL), (0x3ff0000000000000L, 0xbafd75f6467114baL) // 1e-10
        0x4202a05f20000000L, (0xbfef838b8c811c17L, 0xbc8682ec6bde69d5L), (0x3fc63a1a7e0b738aL, 0xbc6744603e3937c7L) // 10000000000.0
        0x40fe240c9fbe76c9L, (0xbfd9393c3aa77f72L, 0xbc44dcbbbdab0b0dL), (0x3fed68efde6f84a3L, 0x3c80f385165de56bL) // 123456.789
        0x4415af1d78b58c40L, (0xbfef838b8c811c17L, 0xbc8682ec6bde69d5L), (0x3fc63a1a7e0b738aL, 0xbc6744603e3937c7L) // 1e+20
        0x7e37e43c8800759cL, (0x0000000000000000L, 0x0000000000000000L), (0x3ff0000000000000L, 0x0000000000000000L) // 1e+300
        0x01a56e1fc2f8f359L, (0x0147f00f350a8dddL, 0x000000000002707dL), (0x3ff0000000000000L, 0x0000000000000000L) // 1e-300
        0x0000000000000001L, (0x0000000000000000L, 0x0000000000000000L), (0x3ff0000000000000L, 0x0000000000000000L) // 5e-324
        0x000012688b70e62bL, (0x000000523fda0e37L, 0x8000000000000000L), (0x3ff0000000000000L, 0x0000000000000000L) // 1e-310
        0x00792a8312c9b5a6L, (0x001c1c5ae019fecfL, 0x0000000000000000L), (0x3ff0000000000000L, 0x0000000000000000L) // 2.2398539135005297e-306
        0x07b0000000000000L, (0x0751df46a2529d39L, 0x03d5c1d8becdd291L), (0x3ff0000000000000L, 0x8000000000000000L) // 1.1830521861667747e-271
        0x07aff7ced916872bL, (0x0751dab35d0a56ecL, 0x83c0ac37ba5deee7L), (0x3ff0000000000000L, 0x8000000000000000L) // 1.181869133980608e-271
        0x07b004189374bc6aL, (0x0751e3d9e79ae386L, 0x83f6c819edf2934dL), (0x3ff0000000000000L, 0x8000000000000000L) // 1.1842352383529414e-271
        0x03d0000000000000L, (0x0371df46a2529d39L, 0x000570762fb374a4L), (0x3ff0000000000000L, 0x8000000000000000L) // 2.5653355008114852e-290
        0x16687e92154ef7acL, (0x160b5c4b6cec0172L, 0x92798d702ffdfe62L), (0x3ff0000000000000L, 0x8000000000000000L) // 1e-200
        0x2b2bff2ee48e0530L, (0x2acf45d208ddd28fL, 0xa7376b81515f597fL), (0x3ff0000000000000L, 0x959e8fdf485698f3L) // 1e-100
        0x405d251b5de9da2bL, (0x3fec9e345cf3114eL, 0xbc224fd253cbd375L), (0xbfdca2eb19742f55L, 0x3c3246534ec8cb8aL) // 116.57979533993846
        0x404b272143622eeeL, (0x3fe9fd1062bb354dL, 0x3c895fa1129f16d1L), (0x3fe2abb51b90496aL, 0x3c8dfbeb3957c963L) // 54.305702612820696
        0x406d4ac3df85884fL, (0xbfe9ff9feeaf5895L, 0x3c8a7bb40fa72840L), (0xbfe2a8241c2c84a0L, 0x3c7e9ff9582ab980L) // 234.33641029434736
        0x403a13ba69f51468L, (0x3fdc22129dcdabc8L, 0xbc75c296a8e2e3b6L), (0x3fecbe1585651bbbL, 0xbc771031378d0c57L) // 26.077063200315393
        0x40681d5c56278c19L, (0xbfcc9d3a2f9693a6L, 0x3c5ad5cf96e6fac1L), (0xbfef30af1dac996aL, 0x3c650a22ffb8c1b4L) // 192.9175215504081
        0x406074bc7fa736f8L, (0x3fe7e96702743043L, 0xbc87ae9d084acf0bL), (0xbfe54403c865b9ebL, 0xbc8ef4bdc4628813L) // 131.64801008853078
        0x4034e12e4fefca70L, (0x3fd6cf594b168b13L, 0xbc720976919e3582L), (0x3fede60adc24e097L, 0x3c8680dbc2e2da62L) // 20.879612918894452
        0x4066d5a8de952a56L, (0xbfa7e977d22b1c95L, 0xbc4a246b9df78146L), (0xbfeff70f9e946844L, 0xbc6e2d3d0415ed60L) // 182.67686394819128
        0x402aff3323bc3964L, (0x3fcde0ab232e89a8L, 0xbc40d295311af4e7L), (0x3fef1db4e0a01c04L, 0x3c8054ffcfb10661L) // 13.498437039114556
        0x40638399289a2afbL, (0x3fd9ea95f384bcd3L, 0xbc584d316a792c96L), (0xbfed424a07a9841fL, 0x3c61b30015ce2da9L) // 156.1124461184589
        0x403925e036d4700bL, (0x3fdb327f8f91f31dL, 0x3c57e23263c83710L), (0x3fecf782365191b2L, 0xbc81169128f59ef9L) // 25.147952486862817
        0x4040540e3f66083cL, (0x3fe1446f964514b6L, 0xbc8eaa68d98a221cL), (0x3feaf10007779c04L, 0xbc2fc92a1fa3b465L) // 32.65668480379142
        0x40631a7607f51e7eL, (0x3fdd3a3f7cf17a84L, 0xbc65f8fbde834a1fL), (0xbfec77db50674ce6L, 0x3c8d0dd20153337fL) // 152.82690809130503
        0x40729aab11a7206dL, (0xbfec575a2bcad502L, 0x3c4e204e01262363L), (0x3fddb78e661fcaf1L, 0xbc59ad48262ff759L) // 297.6667648819337
        0x404648cb5bd14f6eL, (0x3fe674d9e271168aL, 0xbc78f956852f260eL), (0x3fe6cc0ee3dc6e36L, 0x3c6b34c2e5b5e94dL) // 44.568706013872415
        0x4054176cfd9833deL, (0x3fef8c77d312997aL, 0xbc59f686c1d607cdL), (0x3fc56bd5a6c978cdL, 0xbc6d7095447c392fL) // 80.36602725852524
        0x406c3c07dd6783f9L, (0xbfe6f87f867368ebL, 0x3c8d7089b6fe6891L), (0xbfe64762acb063ebL, 0x3c867b14a40b189aL) // 225.87596006601214
        0x407552cdb2bc8a69L, (0xbfd4a6b571cd3c45L, 0x3bd37b58a0460ba8L), (0x3fee49cee630cecbL, 0x3c600f639168bc01L) // 341.17521928452203
        0x4069f839d90b2dfcL, (0xbfddce6a4f462e44L, 0xbc45b4ab80d33f35L), (0xbfec51593be739f4L, 0xbc6c2e2ecfbef6eaL) // 207.7570615022995
        0x4061d9c25247689aL, (0x3fe3584f5044a406L, 0xbc4e5000e247ba1bL), (0xbfe97d9a4ee155dfL, 0x3c57b5ddef260e6cL) // 142.80497087428085
        0x4075f73aba7f2dcdL, (0xbfc306a8a83047b2L, 0x3c54f377641a7ed8L), (0x3fefa4ff4cae83cbL, 0xbc55ea92b8589c00L) // 351.4518380134512
        0x4030c50f520cc78bL, (0x3fd277382f8b76ccL, 0xbc7a4641ece9e189L), (0x3feea39c14ce1d49L, 0xbc6a1ba92cd443daL) // 16.76976502239226
        0x407350c7403fe2c8L, (0xbfe8da0019a3ccbcL, 0xbc49f7f731b68eddL), (0x3fe428cbc48cd537L, 0x3c57ada20d80a31eL) // 309.04864525752464
        0x405a109913b71ed5L, (0x3fef039c55bae7c6L, 0xbc698d32f886f022L), (0xbfcf87214691c31fL, 0x3c6ea9c778f67675L) // 104.25934307940345
        0x4049f74634aa59c4L, (0x3fe93160fa2ca20cL, 0x3c86187609cadf9aL), (0x3fe3bb2d00a14f71L, 0x3c867a61b9e0a767L) // 51.93183000867751
        0x404533ddc7d8767dL, (0x3fe5946f4cebd209L, 0x3c8630f7b50739f9L), (0x3fe7a0ec9b33bc95L, 0x3c659399ec9ba7f5L) // 42.40520570821261
        0x405bc36bd58d8a7bL, (0x3feddd26032127cfL, 0x3c76e2e969cc2d3dL), (0xbfd6fdc4d11dc30bL, 0x3c4ed799168ee5dfL) // 111.05345667669637
        0x40725ce348bb0abdL, (0xbfed470868847581L, 0xbc3d1c826f6ca46eL), (0x3fd9d51f4f617266L, 0x3c74cae9e5482da3L) // 293.80548928321133
        0x405043ef9028cce8L, (0x3fed042f5f4280a9L, 0x3c8ba40c72e602e0L), (0x3fdafc3c65a92fdeL, 0xbc7f68026d8be9a8L) // 65.06149677261749
        0x406a2c08acb688c8L, (0xbfdf650083bc7a4fL, 0xbc5f0a231823cd22L), (0xbfebe2a9cdda2275L, 0xbc886266c90ed17dL) // 209.37605891848784
        0x406cc0487d4e88c6L, (0xbfe8843fce89cb09L, 0xbc653b3ad6f3361fL), (0xbfe490bf31aa3f0cL, 0x3c65ebf533a0e212L) // 230.00884881342625
        0x4060c2050a8c4cceL, (0x3fe6fe8f36622ffbL, 0x3c6596275dd7e572L), (0xbfe64121192eadfdL, 0xbc7aff4836faec4fL) // 134.06311538126323
        0x4068a60428a17082L, (0xbfd2e99acc56d5d6L, 0x3c751cf2c20322ebL), (0xbfee9225af0a0c08L, 0xbc8a1d11dcc528d8L) // 197.1880076554408
        0x40369aa1c66585d2L, (0x3fd8995bd4ea8de8L, 0x3c783452e96041e5L), (0x3fed8ab772c435b9L, 0xbc80eda631c2364cL) // 22.604030990396332
        0x403574d804dafd8cL, (0x3fd76928ef4593e3L, 0x3c69ea0860a4af54L), (0x3fedc843191e2b99L, 0x3c8579b031a918fbL) // 21.45642118784376
        0x40528949eb15ac78L, (0x3feec859b15f3dd6L, 0xbc843d1d6b103be1L), (0x3fd17c221afe10c5L, 0xbc3906ddc47908b7L) // 74.14513661495755
        0x406e9e352b3dad59L, (0xbfecfd15e155f044L, 0xbc8598d840134ba9L), (0xbfdb1ab09e9fb613L, 0x3c72e652019c296cL) // 244.94399034544293
        0x40633ddd053f359dL, (0x3fdc1f6dc7d7313eL, 0xbc7ba4229d4aafe6L), (0xbfecbebb1acab5dbL, 0xbc76bb6635bce2b7L) // 153.93323004098502
        0x405c45f367fceef1L, (0x3fed6f9244e625e7L, 0x3c86e54305108ccfL), (0xbfd91a348600c19eL, 0x3c476630ab323eefL) // 113.09298133564495
        0x406a59ac33f1c16eL, (0xbfe062ef1ce31b8dL, 0xbc82c1119f8dca0bL), (0xbfeb7c6ee7714d0dL, 0x3c828ffa6249fcbcL) // 210.80227086274994
        0xc0c2498ff7043878L, (0xbfabe8b2d272ab60L, 0xbc3d84bfc650e1c2L), (0x3feff3d209aea542L, 0x3c827a7767a7bbb7L) // -9363.12472584493
        0xc0e38dd33856aabeL, (0xbfeff196035b629bL, 0xbc849961be1a58bbL), (0x3fae5bf8fa6636afL, 0xbc43da921dd9c4cbL) // -40046.60062726353
        0x40ecbf7cae86c564L, (0xbfd18788113f7810L, 0xbc68453dbe17a03aL), (0xbfeec6bac36773cbL, 0x3c65948259927db5L) // 58875.89630449822
        0x40e36edc6038f7b4L, (0xbfd4b77a23d58a24L, 0x3c7236ebae45a35fL), (0xbfee46f1e4198459L, 0xbc83e4fc4dcc8281L) // 39798.886745914264
        0xc0e8fd9654d53400L, (0xbfebe7d767cd172bL, 0xbc8918a6ee7387a0L), (0x3fdf5293ac25e90dL, 0x3c75340279a163bcL) // -51180.69785556942
        0x40cd125efb8d1d28L, (0x3fea4b945411ea75L, 0x3c7c9494cecb731eL), (0xbfe23c7acd63608cL, 0xbc831db96de308bdL) // 14884.742051734196
        0x40b3af4cfec1e8b0L, (0xbf88fe40881f5b5cL, 0x3c298ee4bd39f81aL), (0x3fefff63d45b820fL, 0x3c8aae6fdb4c900eL) // 5039.300762290295
        0x40f25137fc5fae46L, (0x3fe131aa471a90a6L, 0x3c4a42065f12f862L), (0xbfeafcfea57120d6L, 0xbc6d7db5d17d2f46L) // 75027.49911468578
        0x40e66821da379b58L, (0x3fc84bed791fe468L, 0xbc5dee527d6d58ccL), (0xbfef6b10a69583f2L, 0x3c89db61e18d65e2L) // 45889.05788784352
        0xc0e4b58e4e0100ccL, (0x3fed93510c26d41aL, 0x3c8cd8c8d62468b2L), (0x3fd86fe2f39f6741L, 0x3c772e996b86a61bL) // -42412.4470219627
        0x40f7722f8310e058L, (0xbfefe134b5c33ef0L, 0xbc46eefc47c1a042L), (0x3fb62d1520e5c5f3L, 0x3c5e74058cbdeff7L) // 96034.96949851641
        0xc0f2a62d82741adfL, (0xbfed6c0d69859202L, 0x3c8938a5931c3e83L), (0x3fd92aaf9eb58216L, 0xbc1829e4ca66a27fL) // -76386.84434900757
        0xc0cffbb7c325f948L, (0xbfb45f47261387f7L, 0xbc45e50138d5bebbL), (0xbfefe6050adb1af4L, 0x3c83850f4f1b3ce9L) // -16375.435642954559
        0x40e91c85f2ffeca4L, (0xbfe926fc2a4544c3L, 0xbc7ab372d2763245L), (0x3fe3c86b2a615f63L, 0xbc81ec7a508c6a4eL) // 51428.18591304988
        0xc0f0fe317d34c3ecL, (0xbfeacf22081d4a35L, 0x3c6c232ec069614dL), (0xbfe178d641bf9520L, 0x3c7b9d8d7f55ac76L) // -69603.09306789906
        0xc0a13ec282e31520L, (0xbfe78c2985d5878fL, 0x3c82878ce4bd3883L), (0x3fe5ab15202f63a3L, 0x3c850d7aa4e523ddL) // -2207.3799048388755
        0xc0f67fe8c706d85fL, (0x3f99efe3804fe98bL, 0xbc34d58259e82c14L), (0x3feffd5f2924b9d4L, 0x3c83f9cbd0bbb105L) // -92158.54859051247
        0x40e06d657b589060L, (0x3fd28759e25e94ffL, 0xbc656686f703f1d8L), (0xbfeea12cb263e496L, 0x3c7a81056937756aL) // 33643.17130687903
        0x40e9d6458b33fc98L, (0xbfb9fd4614c240f2L, 0xbc5e99e163c4043cL), (0x3fefd5acde3d5259L, 0x3c865bf6d510fe4bL) // 52914.173242562625
        0x40cc86981233ad50L, (0xbfdb3ce092d3de1fL, 0x3c76117898b0568dL), (0xbfecf511f8705e45L, 0xbc7d663efbdb2db4L) // 14605.188055476785
    ]

/// (angle, (tan hi, lo)) as bits, computed like `reference`.
let private tanReference =
    [
        0x4045fedacfdb0a48L, (0x3feee4735722e047L, 0x3c43ea5d94e9e55eL) // 43.99105261035271
        0x406110a3d70a3d71L, (0xbfee587c20040bb7L, 0x3c54776619487754L) // 136.52
        0x3ff0000000000000L, (0x3f91dfbd9410a422L, 0x3c18c721220dd6f1L) // 1.0
        0x403e000000000000L, (0x3fe279a74590331cL, 0x3c834863e0792bedL) // 30.0
        0x4046800000000000L, (0x3ff0000000000000L, 0x0000000000000000L) // 45.0
        0x404e000000000000L, (0x3ffbb67ae8584caaL, 0x3c9cec95d0b5c1e3L) // 60.0
        0x405679999999999aL, (0x4081e7a85f79a0cdL, 0x3d0aee1654e7d780L) // 89.9
        0x4056866666666666L, (0xc081e7a85f79a0cdL, 0xbd0aee1654e7d780L) // 90.1
        0x4060e00000000000L, (0xbff0000000000000L, 0x0000000000000000L) // 135.0
        0x4066600000000000L, (0xbf91dfbd9410a422L, 0xbc18c721220dd6f1L) // 179.0
        0x3ee4f8b588e368f1L, (0x3e876ce7d8722eecL, 0x3b18ca3b8f4ab0efL) // 1e-05
        0x4073b2c08fed36e3L, (0xbfefcef5989bd348L, 0xbc8cdb483e43c4adL) // 315.17201225911975
        0x405c3cbe21538d69L, (0xc002e4ba034c4c96L, 0xbc9a12a5ee299988L) // 112.94910462531483
        0x406f49cd787841c2L, (0x400659d5a3dd4026L, 0x3c91378b63f7610dL) // 250.30633185851735
        0x406abf24178b3632L, (0x3fe58ffdc0687f74L, 0x3c62d9e713aadd54L) // 213.97315575780664
        0x406a18648b7b8642L, (0x3fe19091818cf885L, 0x3c66ce9aae1aebceL) // 208.7622735416972
        0x4064877c444304c5L, (0xbfd211bafbbddfddL, 0x3c4f2b50e3f3c3f2L) // 164.23391926850869
        0x4072e636e3f3639cL, (0xbff93926f91a60c9L, 0x3c9fc4c2a956701aL) // 302.3884009845149
        0x4075415cf4a25b35L, (0xbfd72fb6d76b389aL, 0x3c7fbff9fa3e6825L) // 340.08519423885747
        0x4065559ce38bbd96L, (0xbfc50469851bb328L, 0xbc6514535fcf0b9fL) // 170.675401471072
        0x406de3088d5d9997L, (0x3ffaba819c2099bfL, 0x3c91241e028588b0L) // 239.09479397088282
        0x4035d74b60e6f49aL, (0x3fd9a6bbed58a2a2L, 0x3c70482b442e0cebL) // 21.840993934999098
        0x406f9130265dd5a9L, (0x40096e220226d857L, 0x3ca8574a53739cb6L) // 252.5371276695926
        0x406d1eeca5c03e18L, (0x3ff534f23b35466bL, 0x3c9e7b8d185174d5L) // 232.96638762996076
        0x4076583b8c6a7da2L, (0xbfa63967ea7aca1fL, 0x3c424828c93abed9L) // 357.51453820798827
        0x40727e4969d0dc81L, (0xc0007b0402c7d656L, 0xbcae508f0269874fL) // 295.89292317949736
        0x40599d14c04fe357L, (0xc0121c6d9efaa58cL, 0x3cbdaa29c5510485L) // 102.45439155389373
        0x40615c51423d5688L, (0xbfebee2291928b66L, 0xbc8a8fdd7b6acc74L) // 138.8849192808159
        0x406e16e118f3b004L, (0x3ffc87758435485eL, 0x3c842f923a44b3faL) // 240.71497771830775
        0x40203ecc84a88af0L, (0x3fc244cbd8d609c7L, 0xbc48f790b8722784L) // 8.122654100011886
        0x4064c6bacd7fde49L, (0xbfcf6a5859ea99f0L, 0x3c400fa5b19f3471L) // 166.2103030679157
        0x404e3fab573ee582L, (0x3ffc46e4d3718ffdL, 0x3c4e0478123be219L) // 60.49741640635604
        0x404513c6329babf1L, (0x3fecf833e560e43aL, 0x3c8c164214ff4859L) // 42.15448601342349
        0x4035393d41d1c0e0L, (0x3fd8dab266901a84L, 0xbc4eaa65ef3cddc2L) // 21.223590959271746
        0x40714905a2aef374L, (0xc02161a7ffdefb6fL, 0xbccbe6ebfe3afeaaL) // 276.5638758501075
        0x404747ff579cf917L, (0x3ff0e5b886b84101L, 0xbc9c92f8d68e8232L) // 46.56247992672632
        0x4056490bb779f200L, (0x4050ae34fc4a6061L, 0x3cbbda337dd6331dL) // 89.14134013088915
        0x406197bd96a83722L, (0xbfea2713eb862bf9L, 0xbc8b41970fe48493L) // 140.74189312796176
        0x40739b63fc757c1dL, (0xbff0bc6f88141fe3L, 0xbc87e144d8856a2aL) // 313.7119106854678
        0x403d025f6a7c8a1fL, (0x3fe1bea1db4de063L, 0x3c6b5b6f4b31b970L) // 29.009268432049904
        0x406436a38c425e22L, (0xbfd5281f4351d752L, 0x3c5a7eed3f02a59bL) // 161.70746434175913
    ]

/// SHA-256 of the bits of sinCosDeg and tanDeg over a fixed set of angles. It pins the exact
/// results: CI runs this test on Linux and Windows, so any platform difference fails.
let private digest () =
    let angles =
        seq {
            for i in -36000 .. 36000 -> float i / 100.0 // 0.01° steps over two turns
            for i in 0..9999 -> float i * 0.0137 + 1e-7 // irregular angles
            for e in -1070 .. 1020 -> Math.ScaleB(1.2345678901234567, e) // tiny to huge, exact
        }

    let bytes =
        angles
        |> Seq.collect (fun a ->
            let struct (s, c) = Builtins.sinCosDeg a

            [ s; c; Builtins.tanDeg a ]
            |> Seq.collect (fun x -> BitConverter.GetBytes(toBits x)))
        |> Array.ofSeq

    Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()

[<Tests>]
let tests =
    testList
        "Trigonometry"
        [ test "sin and cos are within one ulp of the exact values" {
              for a, s, c in reference do
                  let angle = ofBits a
                  let struct (gs, gc) = Builtins.sinCosDeg angle
                  Expect.isLessThanOrEqual (ulpError gs s) 1.0 $"sin %.17g{angle}"
                  Expect.isLessThanOrEqual (ulpError gc c) 1.0 $"cos %.17g{angle}"
          }

          test "tan is within one ulp of the exact values" {
              for a, t in tanReference do
                  let angle = ofBits a
                  Expect.isLessThanOrEqual (ulpError (Builtins.tanDeg angle) t) 1.0 $"tan %.17g{angle}"
          }

          test "a tiny angle whose product would underflow" {
              // Found in review: Dekker's correction used to underflow here and
              // move a correctly rounded product the wrong way (1.5 ulps off).
              let struct (s, c) = Builtins.sinCosDeg 2.2398539135005297e-306
              Expect.equal (toBits s) 0x001c1c5ae019fecfL "the correctly rounded sine"
              Expect.equal c 1.0 ""
          }

          test "same bits on every platform" {
              Expect.equal (digest ()) "d75b8f7ba76016219c5eda7712eedd39b80784785d5610895c9050513443b20d" "sinCosDeg or tanDeg changed, or differs on this platform"
          }

          test "exact at multiples of 90 degrees, with +0 for zeros" {
              for k in -8 .. 8 do
                  let a = 90.0 * float k
                  let struct (s, c) = Builtins.sinCosDeg a
                  let es, ec = [| 0.0, 1.0; 1.0, 0.0; 0.0, -1.0; -1.0, 0.0 |].[((k % 4) + 4) % 4]
                  Expect.equal (toBits s) (toBits es) $"sin %g{a}"
                  Expect.equal (toBits c) (toBits ec) $"cos %g{a}"
          }

          test "sin and tan are odd and cos is even, exactly" {
              for i in 0..20000 do
                  let a = float i * 0.0731
                  let struct (s, c) = Builtins.sinCosDeg a
                  let struct (ns, nc) = Builtins.sinCosDeg -a
                  Expect.equal (toBits ns) (toBits (-s + 0.0)) $"sin -%g{a}"
                  Expect.equal (toBits nc) (toBits c) $"cos -%g{a}"
                  Expect.equal (toBits (Builtins.tanDeg -a)) (toBits (-(Builtins.tanDeg a) + 0.0)) $"tan -%g{a}"
          }

          test "sinDeg and cosDeg agree with sinCosDeg" {
              for a in [ 0.0; 18.95; 137.5; -222.5; 1e20 ] do
                  let struct (s, c) = Builtins.sinCosDeg a
                  Expect.equal (toBits (Builtins.sinDeg a)) (toBits s) ""
                  Expect.equal (toBits (Builtins.cosDeg a)) (toBits c) ""
          }

          test "tan is exact at multiples of 90 degrees: 0, or infinite with the sign of sin / cos" {
              for k in -8 .. 8 do
                  let a = 90.0 * float k
                  let expected = [| 0.0; infinity; 0.0; -infinity |].[((k % 4) + 4) % 4]
                  Expect.equal (toBits (Builtins.tanDeg a)) (toBits expected) $"tan %g{a}"

              Expect.equal (Builtins.tanDeg 45.0) 1.0 "tan 45"
          }

          test "tan is the reciprocal near the poles" {
              // Odd quadrants use -1/tan, evaluated accurately by the kernel.
              // Dyadic angles, so 90 - x is exact.
              for x in [ Math.ScaleB(1.0, -30); 0.125; 1.0; 30.0; 44.5 ] do
                  let t = Builtins.tanDeg x
                  let cot = Builtins.tanDeg(90.0 - x)
                  Expect.floatClose Accuracy.veryHigh (t * cot) 1.0 $"tan %g{x} tan(90 - %g{x})"
          }

          test "large angles are reduced exactly" {
              for a in [ 1e20; 123456.789; 1e300; 7.5e15 ] do
                  let struct (s, c) = Builtins.sinCosDeg a
                  let struct (rs, rc) = Builtins.sinCosDeg(a % 360.0)
                  Expect.equal (toBits s) (toBits rs) $"sin %g{a}"
                  Expect.equal (toBits c) (toBits rc) $"cos %g{a}"
                  Expect.equal (toBits (Builtins.tanDeg a)) (toBits (Builtins.tanDeg(a % 360.0))) $"tan %g{a}"
          }

          test "infinite and NaN angles give NaN" {
              for a in [ Double.PositiveInfinity; Double.NegativeInfinity; nan ] do
                  let struct (s, c) = Builtins.sinCosDeg a
                  Expect.isTrue (Double.IsNaN s && Double.IsNaN c && Double.IsNaN(Builtins.tanDeg a)) $"%g{a}"
          } ]
