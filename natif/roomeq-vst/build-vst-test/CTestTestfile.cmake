# CMake generated Testfile for 
# Source directory: /home/z/my-project/workspace/RoomEq/natif/roomeq-vst
# Build directory: /home/z/my-project/workspace/RoomEq/natif/roomeq-vst/build-vst-test
# 
# This file includes the relevant testing commands required for 
# testing this directory and lists subdirectories to be tested as well.
add_test("roomeq_vst_core_tests" "/home/z/my-project/workspace/RoomEq/natif/roomeq-vst/build-vst-test/roomeq_vst_tests" "/home/z/my-project/workspace/RoomEq/natif/roomeq-vst/../roomeq-au/tests/data")
set_tests_properties("roomeq_vst_core_tests" PROPERTIES  _BACKTRACE_TRIPLES "/home/z/my-project/workspace/RoomEq/natif/roomeq-vst/CMakeLists.txt;93;add_test;/home/z/my-project/workspace/RoomEq/natif/roomeq-vst/CMakeLists.txt;0;")
add_test("roomeq_vst_host_abi" "/home/z/my-project/workspace/RoomEq/natif/roomeq-vst/build-vst-test/roomeq_vst_host" "/home/z/my-project/workspace/RoomEq/natif/roomeq-vst/build-vst-test/RoomEqVst.so" "/home/z/my-project/workspace/RoomEq/natif/roomeq-vst/../roomeq-au/tests/data")
set_tests_properties("roomeq_vst_host_abi" PROPERTIES  _BACKTRACE_TRIPLES "/home/z/my-project/workspace/RoomEq/natif/roomeq-vst/CMakeLists.txt;103;add_test;/home/z/my-project/workspace/RoomEq/natif/roomeq-vst/CMakeLists.txt;0;")
